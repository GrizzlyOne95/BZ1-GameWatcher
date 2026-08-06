#!/usr/bin/env python3
"""Generate the Angular stock-vehicle catalog from Battlezone ODF files.

Usage:
    python tools/fetch-battlezone-wiki-renders.py
    python tools/build-stock-vehicle-catalog.py /path/to/stock/odf

ODF keys are case-insensitive. GameObjectClass values are inherited through baseName from the oldest
ancestor to the leaf. A craft uses its exact wiki render when available, otherwise the nearest base
ODF render from Web/public/vehicles/manifest.json. Unknown and modded craft remain uncatalogued.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

SECTION_RE = re.compile(r"^\s*\[([^]]+)]\s*$")
PROPERTY_RE = re.compile(r"^\s*([^=;]+?)\s*=\s*(.*?)\s*$")
VEHICLE_CLASS_LABELS = {
    "wingman",
    "hover",
    "turrettank",
    "walker",
    "apc",
    "scavenger",
    "constructor",
    "recycler",
    "factory",
    "armory",
    "artillery",
    "bomber",
    "commvehicle",
    "constructionrig",
    "craft",
    "deployable",
    "howitzer",
    "minelayer",
    "person",
    "repair",
    "service",
    "tank",
    "trackedvehicle",
}


@dataclass(frozen=True)
class Odf:
    code: str
    path: Path
    game_object: dict[str, str]


def read_text(path: Path) -> str:
    data = path.read_bytes()
    for encoding in ("utf-8-sig", "cp1252", "latin-1"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    raise ValueError(f"Unable to decode {path}")


def unquote(value: str) -> str:
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in {'\"', "'"}:
        return value[1:-1]
    return value


def parse_odf(path: Path) -> Odf:
    current_section = ""
    sections: dict[str, dict[str, str]] = {}

    for raw_line in read_text(path).splitlines():
        line = raw_line.split("//", 1)[0].split(";", 1)[0].strip()
        if not line:
            continue

        section_match = SECTION_RE.match(line)
        if section_match:
            current_section = section_match.group(1).strip().lower()
            sections.setdefault(current_section, {})
            continue

        property_match = PROPERTY_RE.match(line)
        if property_match and current_section:
            sections[current_section][property_match.group(1).strip().lower()] = unquote(
                property_match.group(2)
            )

    return Odf(path.stem.lower(), path, sections.get("gameobjectclass", {}))


def load_odfs(root: Path) -> dict[str, Odf]:
    odfs: dict[str, Odf] = {}
    for path in sorted(root.rglob("*.odf"), key=lambda item: str(item).lower()):
        odf = parse_odf(path)
        if odf.code in odfs:
            print(
                f"warning: duplicate {odf.code}.odf; keeping {odfs[odf.code].path}, ignoring {path}",
                file=sys.stderr,
            )
            continue
        odfs[odf.code] = odf
    return odfs


def inheritance_chain(code: str, odfs: dict[str, Odf]) -> list[Odf]:
    leaf_to_root: list[Odf] = []
    seen: set[str] = set()
    current = code

    while current:
        if current in seen:
            raise ValueError(f"Inheritance cycle while resolving {code}: {current}")
        seen.add(current)

        odf = odfs.get(current)
        if odf is None:
            if leaf_to_root:
                print(f"warning: {leaf_to_root[-1].code} references missing base {current}", file=sys.stderr)
                break
            raise KeyError(code)

        leaf_to_root.append(odf)
        current = odf.game_object.get("basename", "").strip().lower().removesuffix(".odf")

    return list(reversed(leaf_to_root))


def resolve_game_object(chain: list[Odf]) -> dict[str, str]:
    resolved: dict[str, str] = {}
    for odf in chain:
        resolved.update(odf.game_object)
    return resolved


def load_image_manifest(path: Path) -> dict[str, dict[str, str]]:
    if not path.exists():
        return {}
    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        raise ValueError(f"Image manifest must contain an object: {path}")
    return {
        str(code).lower(): details
        for code, details in raw.items()
        if isinstance(details, dict)
    }


def resolve_image(chain: list[Odf], images: dict[str, dict[str, str]]) -> tuple[str | None, str | None]:
    for odf in reversed(chain):
        details = images.get(odf.code)
        if not details:
            continue
        thumbnail_url = details.get("thumbnailUrl")
        source_url = details.get("sourceUrl")
        return (
            thumbnail_url if isinstance(thumbnail_url, str) else None,
            source_url if isinstance(source_url, str) else None,
        )
    return None, None


def nullable_number(value: str | None) -> int | float | None:
    if value in (None, ""):
        return None
    try:
        number = float(value)
    except ValueError:
        return None
    return int(number) if number.is_integer() else number


def nullable_text(value: str | None) -> str | None:
    return value if value not in (None, "") else None


def vehicle_definition(
    code: str,
    values: dict[str, str],
    thumbnail_url: str | None,
    thumbnail_source_url: str | None,
) -> dict[str, object] | None:
    class_label = values.get("classlabel", "").lower()
    unit_name = nullable_text(values.get("unitname"))
    if not unit_name or class_label not in VEHICLE_CLASS_LABELS:
        return None

    weapons = []
    for slot in range(1, 9):
        hardpoint = nullable_text(values.get(f"weaponhard{slot}"))
        weapon = nullable_text(values.get(f"weaponname{slot}"))
        if hardpoint is not None or weapon is not None:
            weapons.append({"slot": slot, "hardpoint": hardpoint, "odf": weapon})

    return {
        "code": code,
        "unitName": unit_name,
        "baseName": nullable_text(values.get("basename")),
        "classLabel": nullable_text(values.get("classlabel")),
        "scrapValue": nullable_number(values.get("scrapvalue")),
        "scrapCost": nullable_number(values.get("scrapcost")),
        "buildTime": nullable_number(values.get("buildtime")),
        "maxHealth": nullable_number(values.get("maxhealth")),
        "maxAmmo": nullable_number(values.get("maxammo")),
        "aiName": nullable_text(values.get("ainame")),
        "aiName2": nullable_text(values.get("ainame2")),
        "heatSignature": nullable_number(values.get("heatsignature")),
        "imageSignature": nullable_number(values.get("imagesignature")),
        "radarSignature": nullable_number(values.get("radarsignature")),
        "weaponMask": nullable_text(values.get("weaponmask")),
        "thumbnailUrl": thumbnail_url,
        "thumbnailSourceUrl": thumbnail_source_url,
        "weapons": weapons,
    }


def iter_vehicle_definitions(
    odfs: dict[str, Odf],
    images: dict[str, dict[str, str]],
) -> Iterable[dict[str, object]]:
    for code in sorted(odfs):
        chain = inheritance_chain(code, odfs)
        thumbnail_url, source_url = resolve_image(chain, images)
        definition = vehicle_definition(
            code,
            resolve_game_object(chain),
            thumbnail_url,
            source_url,
        )
        if definition:
            yield definition


def typescript_literal(value: object, indent: int = 0) -> str:
    prefix = " " * indent
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return str(value)
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=False)
    if isinstance(value, list):
        if not value:
            return "[]"
        items = ",\n".join(
            f"{' ' * (indent + 4)}{typescript_literal(item, indent + 4)}" for item in value
        )
        return f"[\n{items}\n{prefix}]"
    if isinstance(value, dict):
        if not value:
            return "{}"
        items = ",\n".join(
            f"{' ' * (indent + 4)}{key}: {typescript_literal(item, indent + 4)}"
            for key, item in value.items()
        )
        return f"{{\n{items}\n{prefix}}}"
    raise TypeError(type(value))


def render(definitions: Iterable[dict[str, object]]) -> str:
    rows = ",\n".join(
        f"    {definition['code']}: {typescript_literal(definition, 4)}"
        for definition in definitions
    )
    return (
        "// Generated by tools/build-stock-vehicle-catalog.py. Do not edit by hand.\n\n"
        "import type { StockVehicleDefinition } from './stock-vehicles';\n\n"
        "export const GENERATED_STOCK_VEHICLES: "
        "Readonly<Record<string, StockVehicleDefinition>> = Object.freeze({\n"
        f"{rows}\n"
        "});\n"
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("odf_root", type=Path)
    parser.add_argument(
        "--image-manifest",
        type=Path,
        default=Path("Web/public/vehicles/manifest.json"),
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("Web/src/app/data/stock-vehicles.generated.ts"),
    )
    args = parser.parse_args()

    if not args.odf_root.is_dir():
        parser.error(f"ODF root does not exist or is not a directory: {args.odf_root}")

    odfs = load_odfs(args.odf_root)
    images = load_image_manifest(args.image_manifest)
    definitions = list(iter_vehicle_definitions(odfs, images))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(render(definitions), encoding="utf-8")
    print(
        f"Wrote {len(definitions)} stock vehicle definitions using {len(images)} image entries "
        f"to {args.output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

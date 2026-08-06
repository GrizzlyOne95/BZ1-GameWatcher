#!/usr/bin/env python3
"""Generate the Angular stock-vehicle catalog from a directory of Battlezone ODF files.

Usage:
    python tools/build-stock-vehicle-catalog.py /path/to/stock/odf \
        --output Web/src/app/data/stock-vehicles.generated.ts

ODF keys are case-insensitive. Values inherited through GameObjectClass.baseName are resolved from
oldest ancestor to leaf, with the leaf overriding inherited properties. Cycles and missing bases are
reported instead of silently producing misleading data.
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
}

CATALOG_KEYS = (
    "basename",
    "classlabel",
    "scrapvalue",
    "scrapcost",
    "buildtime",
    "maxhealth",
    "maxammo",
    "unitname",
    "ainame",
    "ainame2",
    "heatsignature",
    "imagesignature",
    "radarsignature",
    "weaponmask",
)


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
        line = raw_line.split("//", 1)[0].strip()
        if not line or line.startswith(";"):
            continue

        section_match = SECTION_RE.match(line)
        if section_match:
            current_section = section_match.group(1).strip().lower()
            sections.setdefault(current_section, {})
            continue

        property_match = PROPERTY_RE.match(line)
        if property_match and current_section:
            key = property_match.group(1).strip().lower()
            value = unquote(property_match.group(2).strip())
            sections[current_section][key] = value

    return Odf(
        code=path.stem.lower(),
        path=path,
        game_object=sections.get("gameobjectclass", {}),
    )


def load_odfs(root: Path) -> dict[str, Odf]:
    odfs: dict[str, Odf] = {}
    for path in sorted(root.rglob("*.odf"), key=lambda item: str(item).lower()):
        odf = parse_odf(path)
        existing = odfs.get(odf.code)
        if existing:
            print(
                f"warning: duplicate {odf.code}.odf; keeping {existing.path}, ignoring {path}",
                file=sys.stderr,
            )
            continue
        odfs[odf.code] = odf
    return odfs


def inheritance_chain(code: str, odfs: dict[str, Odf]) -> list[Odf]:
    chain: list[Odf] = []
    seen: set[str] = set()
    current = code

    while current:
        if current in seen:
            raise ValueError(f"Inheritance cycle while resolving {code}: {current}")
        seen.add(current)

        odf = odfs.get(current)
        if odf is None:
            if chain:
                print(f"warning: {chain[-1].code} references missing base {current}", file=sys.stderr)
                break
            raise KeyError(code)

        chain.append(odf)
        current = odf.game_object.get("basename", "").strip().lower().removesuffix(".odf")

    chain.reverse()
    return chain


def resolve_game_object(code: str, odfs: dict[str, Odf]) -> dict[str, str]:
    resolved: dict[str, str] = {}
    for odf in inheritance_chain(code, odfs):
        resolved.update(odf.game_object)
    return resolved


def nullable_number(value: str | None) -> int | float | None:
    if value is None or value == "":
        return None
    try:
        number = float(value)
    except ValueError:
        return None
    return int(number) if number.is_integer() else number


def nullable_text(value: str | None) -> str | None:
    return value if value not in (None, "") else None


def vehicle_definition(code: str, values: dict[str, str]) -> dict[str, object] | None:
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
        "weapons": weapons,
    }


def iter_vehicle_definitions(odfs: dict[str, Odf]) -> Iterable[dict[str, object]]:
    for code in sorted(odfs):
        values = resolve_game_object(code, odfs)
        definition = vehicle_definition(code, values)
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
    definitions = list(definitions)
    rows = ",\n".join(
        f"    {definition['code']}: {typescript_literal(definition, 4)}"
        for definition in definitions
    )
    return f"""// Generated by tools/build-stock-vehicle-catalog.py. Do not edit by hand.\n\nimport {{ StockVehicleDefinition }} from './stock-vehicles';\n\nexport const GENERATED_STOCK_VEHICLES: Readonly<Record<string, StockVehicleDefinition>> = Object.freeze({{\n{rows}\n}});\n"""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("odf_root", type=Path)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("Web/src/app/data/stock-vehicles.generated.ts"),
    )
    args = parser.parse_args()

    if not args.odf_root.is_dir():
        parser.error(f"ODF root does not exist or is not a directory: {args.odf_root}")

    odfs = load_odfs(args.odf_root)
    definitions = list(iter_vehicle_definitions(odfs))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(render(definitions), encoding="utf-8")
    print(f"Wrote {len(definitions)} stock vehicle definitions to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, re, sys
from dataclasses import dataclass
from pathlib import Path

SECTION_RE = re.compile(r"^\s*\[([^]]+)]\s*$")
PROPERTY_RE = re.compile(r"^\s*([^=;]+?)\s*=\s*(.*?)\s*$")
VEHICLE_CLASS_LABELS = {
    "wingman", "hover", "turrettank", "walker", "apc", "scavenger",
    "constructor", "recycler", "factory", "armory", "artillery", "bomber",
    "commvehicle", "constructionrig", "craft", "deployable", "howitzer",
    "minelayer", "person", "repair", "service", "tank", "trackedvehicle",
    "tug", "sav", "turret",
}
FACTIONS = {"av":"nsdf", "bv":"blackDog", "sv":"cca", "cv":"cra"}

@dataclass(frozen=True)
class Odf:
    code: str
    path: Path
    game_object: dict[str,str]

def read_text(path: Path) -> str:
    data=path.read_bytes()
    for enc in ("utf-8-sig","cp1252","latin-1"):
        try: return data.decode(enc)
        except UnicodeDecodeError: pass
    raise ValueError(f"Unable to decode {path}")

def unquote(value: str) -> str:
    value=value.strip()
    if len(value)>=2 and value[0]==value[-1] and value[0] in {'"',"'"}:
        return value[1:-1]
    return value

def parse_odf(path: Path) -> Odf:
    current=""; sections:dict[str,dict[str,str]]={}
    for raw in read_text(path).splitlines():
        line=raw.split("//",1)[0].split(";",1)[0].strip()
        if not line: continue
        match=SECTION_RE.match(line)
        if match:
            current=match.group(1).strip().lower(); sections.setdefault(current,{})
            continue
        match=PROPERTY_RE.match(line)
        if match and current:
            sections[current][match.group(1).strip().lower()]=unquote(match.group(2))
    return Odf(path.stem.lower(), path, sections.get("gameobjectclass",{}))

def load_odfs(root:Path)->dict[str,Odf]:
    out={}
    for path in sorted(root.rglob("*.odf"), key=lambda item:str(item).lower()):
        odf=parse_odf(path)
        if odf.code in out:
            print(f"warning: duplicate {odf.code}; keeping {out[odf.code].path}, ignoring {path}",file=sys.stderr)
            continue
        out[odf.code]=odf
    return out

def inheritance_chain(code:str, odfs:dict[str,Odf])->list[Odf]:
    leaf_to_root=[]; seen=set(); current=code
    while current:
        if current in seen:
            raise ValueError(f"Inheritance cycle while resolving {code}: {current}")
        seen.add(current)
        odf=odfs.get(current)
        if odf is None:
            if leaf_to_root:
                print(f"warning: {leaf_to_root[-1].code} references missing base {current}",file=sys.stderr)
                break
            raise KeyError(code)
        leaf_to_root.append(odf)
        base=odf.game_object.get("basename","").strip().lower().removesuffix(".odf")
        # Two stock artillery ODFs self-reference; treat this as a terminal root, not a cycle.
        if not base or base == current:
            break
        current=base
    return list(reversed(leaf_to_root))

def resolve(chain:list[Odf])->dict[str,str]:
    values={}
    for odf in chain: values.update(odf.game_object)
    return values

def load_manifest(path:Path)->dict[str,dict[str,str]]:
    if not path.exists(): return {}
    raw=json.loads(path.read_text(encoding='utf-8'))
    if not isinstance(raw,dict): raise ValueError('manifest must be object')
    return {str(key).lower():value for key,value in raw.items() if isinstance(value,dict)}

def resolve_image(chain, images):
    for odf in reversed(chain):
        details=images.get(odf.code)
        if details:
            thumb=details.get('thumbnailUrl'); source=details.get('sourceUrl')
            return (thumb if isinstance(thumb,str) else None, source if isinstance(source,str) else None)
    return None,None

def nullable_number(value):
    if value in (None,''): return None
    try:number=float(value)
    except ValueError:return None
    return int(number) if number.is_integer() else number

def nullable_text(value): return value if value not in (None,'') else None

def vehicle_definition(code, leaf, values, thumb, source):
    class_label=values.get('classlabel','').lower(); unit=nullable_text(values.get('unitname'))
    if not unit or class_label not in VEHICLE_CLASS_LABELS:return None
    weapons=[]
    for slot in range(1,9):
        hard=nullable_text(values.get(f'weaponhard{slot}')); weapon=nullable_text(values.get(f'weaponname{slot}'))
        if hard is not None or weapon is not None: weapons.append({'slot':slot,'hardpoint':hard,'odf':weapon})
    direct_base=nullable_text(leaf.game_object.get('basename'))
    if direct_base and direct_base.lower().removesuffix('.odf') == code:
        direct_base=None
    return {
      'code':code,
      'unitName':unit,
      'faction':FACTIONS.get(code[:2]),
      'baseName':direct_base,
      'classLabel':nullable_text(values.get('classlabel')),
      'scrapValue':nullable_number(values.get('scrapvalue')),
      'scrapCost':nullable_number(values.get('scrapcost')),
      'buildTime':nullable_number(values.get('buildtime')),
      'maxHealth':nullable_number(values.get('maxhealth')),
      'maxAmmo':nullable_number(values.get('maxammo')),
      'aiName':nullable_text(values.get('ainame')),
      'aiName2':nullable_text(values.get('ainame2')),
      'heatSignature':nullable_number(values.get('heatsignature')),
      'imageSignature':nullable_number(values.get('imagesignature')),
      'radarSignature':nullable_number(values.get('radarsignature')),
      'weaponMask':nullable_text(values.get('weaponmask')),
      'thumbnailUrl':thumb,
      'thumbnailSourceUrl':source,
      'weapons':weapons,
    }

def iter_defs(odfs,images):
    for code in sorted(odfs):
        chain=inheritance_chain(code,odfs)
        thumb,source=resolve_image(chain,images)
        definition=vehicle_definition(code,odfs[code],resolve(chain),thumb,source)
        if definition:yield definition

def write_split_catalog(output: Path, definitions: list[dict[str, object]]) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    row_fields = [
        "code", "unitName", "faction", "baseName", "classLabel", "scrapValue", "scrapCost",
        "buildTime", "maxHealth", "maxAmmo", "aiName", "aiName2", "heatSignature",
        "imageSignature", "radarSignature", "weaponMask", "thumbnailUrl", "thumbnailSourceUrl",
    ]
    rows = []
    for definition in definitions:
        row = [definition[field] for field in row_fields]
        row.append([
            [weapon["slot"], weapon["hardpoint"], weapon["odf"]]
            for weapon in definition["weapons"]
        ])
        rows.append(row)

    chunk_count = 4
    for index in range(chunk_count):
        chunk = rows[index::chunk_count]
        module_path = output.with_name(f"stock-vehicles.rows-{index + 1:02d}.generated.ts")
        module_path.write_text(
            "// Generated by tools/build-stock-vehicle-catalog.py. Do not edit by hand.\n\n"
            f"export const STOCK_VEHICLE_ROWS_{index + 1:02d} = "
            + json.dumps(chunk, separators=(",", ":"), ensure_ascii=False)
            + " as const;\n",
            encoding="utf-8",
        )

    output.write_text(
        """// Generated by tools/build-stock-vehicle-catalog.py. Do not edit by hand.

import type { StockFactionKey, StockVehicleDefinition } from './stock-vehicles';
import { STOCK_VEHICLE_ROWS_01 } from './stock-vehicles.rows-01.generated';
import { STOCK_VEHICLE_ROWS_02 } from './stock-vehicles.rows-02.generated';
import { STOCK_VEHICLE_ROWS_03 } from './stock-vehicles.rows-03.generated';
import { STOCK_VEHICLE_ROWS_04 } from './stock-vehicles.rows-04.generated';

type StockVehicleWeaponRow = readonly [number, string | null, string | null];
type StockVehicleRow = readonly [
    string, string, StockFactionKey | null, string | null, string | null,
    number | null, number | null, number | null, number | null, number | null,
    string | null, string | null, number | null, number | null, number | null,
    string | null, string | null, string | null, readonly StockVehicleWeaponRow[]
];

const rows: readonly StockVehicleRow[] = [
    ...STOCK_VEHICLE_ROWS_01,
    ...STOCK_VEHICLE_ROWS_02,
    ...STOCK_VEHICLE_ROWS_03,
    ...STOCK_VEHICLE_ROWS_04
];

export const GENERATED_STOCK_VEHICLES: Readonly<Record<string, StockVehicleDefinition>> =
    Object.freeze(Object.fromEntries(rows.map(row => [row[0], {
        code: row[0],
        unitName: row[1],
        faction: row[2],
        baseName: row[3],
        classLabel: row[4],
        scrapValue: row[5],
        scrapCost: row[6],
        buildTime: row[7],
        maxHealth: row[8],
        maxAmmo: row[9],
        aiName: row[10],
        aiName2: row[11],
        heatSignature: row[12],
        imageSignature: row[13],
        radarSignature: row[14],
        weaponMask: row[15],
        thumbnailUrl: row[16],
        thumbnailSourceUrl: row[17],
        weapons: row[18].map(weapon => ({ slot: weapon[0], hardpoint: weapon[1], odf: weapon[2] }))
    }])));
""",
        encoding="utf-8",
    )

def main():
    parser=argparse.ArgumentParser(); parser.add_argument('odf_root',type=Path); parser.add_argument('--image-manifest',type=Path,default=Path('Web/public/vehicles/manifest.json')); parser.add_argument('--output',type=Path,default=Path('Web/src/app/data/stock-vehicles.generated.ts')); args=parser.parse_args()
    if not args.odf_root.is_dir(): parser.error(f"ODF root does not exist or is not a directory: {args.odf_root}")
    odfs=load_odfs(args.odf_root); images=load_manifest(args.image_manifest); definitions=list(iter_defs(odfs,images)); write_split_catalog(args.output, definitions); print(f'Wrote {len(definitions)} stock vehicle definitions using {len(images)} image entries to {args.output} and row modules')
    return 0

if __name__=='__main__': raise SystemExit(main())

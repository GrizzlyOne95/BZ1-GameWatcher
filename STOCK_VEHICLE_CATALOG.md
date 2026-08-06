# Stock vehicle catalog

The public site does not contain the original stock ODF files. Instead, the generated TypeScript
catalog is built from a local/exported ODF directory:

```bash
python tools/fetch-battlezone-wiki-renders.py
python tools/build-stock-vehicle-catalog.py /path/to/odf
```

## Current generation

The current catalog was generated from 231 supplied stock ODF files:

- 229 named definitions were included.
- 2 unnamed files (`cvwo003.odf` and `cvwo004.odf`) were excluded because they expose neither a
  usable `unitName` nor a supported `classLabel`.
- `cvartl.odf` and `svartl.odf` self-reference through `baseName`; the generator treats those stock
  roots as terminal rather than reporting an inheritance cycle.

Faction mapping is based on the stock filename prefix:

| Prefix | Faction |
| --- | --- |
| `av` | NSDF |
| `bv` | Black Dog |
| `sv` | CCA |
| `cv` | CRA |

The generated catalog currently contains 53 NSDF, 49 Black Dog, 67 CCA, and 60 CRA entries.
Unknown and modded ODF codes remain uncatalogued and are displayed verbatim by the site.

## Images

Vehicle renders are optional. The generator selects an exact ODF image when one is available, then
walks the resolved `baseName` chain for the nearest inherited image. It never assigns a render based
only on a similar-looking name.

Faction emblem source URLs are retained in `Web/public/factions/manifest.json` and
`Web/src/app/data/stock-vehicles.ts`.

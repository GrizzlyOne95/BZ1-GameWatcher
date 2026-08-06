# Vehicle thumbnails

This directory is populated by `tools/fetch-battlezone-wiki-renders.py`.

The importer downloads small identification thumbnails from the Battlezone Wiki's **Battlezone
(1998) Renders** category and writes `manifest.json`. Render filenames generally mirror stock ODF
names (`Bvrdev render.png` -> `bvrdev`), so the ODF catalog generator can associate exact craft or
inherit the nearest pictured `baseName`.

Run:

```bash
python tools/fetch-battlezone-wiki-renders.py
python tools/build-stock-vehicle-catalog.py /path/to/stock/odf
```

The software license for this repository does **not** grant rights to third-party artwork. Battlezone
renders and trademarks remain the property of their respective owners. The site uses reduced-size
renders only to identify the craft being reported by the public game lobby. Keep the source URL in
`manifest.json` and the visible source link in the UI when adding or replacing an image.

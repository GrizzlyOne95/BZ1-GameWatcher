# Vehicle thumbnails

This directory is populated by `tools/fetch-battlezone-wiki-renders.py`.

The importer downloads small identification thumbnails from the Battlezone Wiki's **Battlezone
(1998) Renders** category and writes `manifest.json`. Render filenames generally mirror stock ODF
names (`Avtank render.png` -> `avtank`), so the ODF catalog generator can associate an exact craft
or inherit the nearest pictured `baseName`.

Some useful renders are attached to individual vehicle pages but are not members of the main render
category. Declare those in `wiki-overrides.json`. The Red Devil is the first example: lobby craft
`bvrmpa` inherits from `bvrdev`, and `bvrdev` is explicitly associated with the wiki's
`Bvrdev render.png` image.

Run:

```bash
python tools/fetch-battlezone-wiki-renders.py
python tools/build-stock-vehicle-catalog.py /path/to/stock/odf
```

For a focused additive refresh:

```bash
python tools/fetch-battlezone-wiki-renders.py --codes bvrdev avtank
```

The software license for this repository does **not** grant rights to third-party artwork. Battlezone
renders and trademarks remain the property of their respective owners. The site uses reduced-size
renders only to identify the craft being reported by the public game lobby. Keep the source URL in
`manifest.json` and the visible source link in the UI when adding or replacing an image.

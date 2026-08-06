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

## Source priority

Use the following order so a plausible-looking image is never assigned to the wrong ODF:

1. Exact ODF render from the Battlezone Wiki.
2. Nearest rendered base ODF in the resolved `baseName` inheritance chain.
3. A manually reviewed crop from an official game manual when no suitable wiki render exists.
4. No image. Do not guess from a similar unit name or chassis.

Official manual fallbacks:

- Battlezone 98 Redux manual:
  `https://cdn.akamai.steamstatic.com/steam/apps/301650/manuals/BZ98R_Manual_GB.pdf?t=1461330226`
- The Red Odyssey manual:
  `https://cdn.akamai.steamstatic.com/steam/apps/470750/manuals/TheRedOdyssey_Manual.pdf?t=1579791115`

For a manual-derived image, render the source page at high resolution, crop only the unit artwork,
save it as `/vehicles/<odf-code>.png`, and add a manifest entry whose `sourceUrl` points to the
official PDF with a page fragment where supported, for example `...Manual.pdf#page=8`. Record the
unit/page mapping during review rather than attempting blind automatic crops across every page.

## Generate the catalog

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

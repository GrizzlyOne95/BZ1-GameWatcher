# Vehicle thumbnails

The optimized thumbnails in this directory are committed repository assets and are served by the
Game Watcher itself from `/vehicles/*`. Once the cache has been populated and passes validation,
production builds use it without contacting the Battlezone Wiki. Until that first seed completes,
the Render image performs a best-effort refresh so the existing site does not lose its renders.

`tools/fetch-battlezone-wiki-renders.py` is an explicit maintenance utility. It downloads reduced-size
identification renders, writes each successful download into this directory, and updates
`manifest.json`. A failed or partial refresh preserves every previously committed image and manifest
entry rather than replacing it with a broken external URL or `null`.

Render filenames generally mirror stock ODF names (`Avtank render.png` -> `avtank`), so the catalog
can associate an exact craft or inherit the nearest pictured `baseName`. The importer scans both the
base-game and The Red Odyssey render categories, which covers NSDF, CCA, Black Dog, and CRA craft.

Some useful renders are attached to individual vehicle pages but are not members of those categories.
Declare those in `wiki-overrides.json`. For example, the NSDF Rat Pack uses the otherwise uncategorized
`Avapc render.png`, while Red Devil variants inherit from the `bvrdev` image.

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

## Refresh the committed render set

The standard refresh is limited to ODF codes present in the generated stock catalog:

```bash
python tools/fetch-battlezone-wiki-renders.py --force
node Web/tools/generate-vehicle-images.mjs
python tools/fetch-battlezone-wiki-renders.py --verify-only
```

For a focused additive refresh:

```bash
python tools/fetch-battlezone-wiki-renders.py --codes avapc cvhraz --force
```

To inspect matches without downloading:

```bash
python tools/fetch-battlezone-wiki-renders.py --dry-run
```

The **Refresh vehicle renders** GitHub Actions workflow runs the same refresh and commits changed
images, the manifest, and the generated TypeScript lookup. After that commit exists, normal
application builds are network-independent for unit artwork.

The software license for this repository does **not** grant rights to third-party artwork. Battlezone
renders and trademarks remain the property of their respective owners. The site uses reduced-size
renders only to identify the craft being reported by the public game lobby. Keep the source URL in
`manifest.json` and the visible source link in the UI when adding or replacing an image.

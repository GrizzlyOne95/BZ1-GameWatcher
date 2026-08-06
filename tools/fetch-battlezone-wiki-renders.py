#!/usr/bin/env python3
"""Download small Battlezone 1998 render thumbnails from the Battlezone Wiki.

The wiki render filenames generally mirror stock ODF names, for example:
`Bvrdev render.png` -> `bvrdev`. The resulting manifest lets the ODF catalog generator associate
leaf variants with an exact image or inherit the nearest pictured base ODF.

Usage:
    python tools/fetch-battlezone-wiki-renders.py
    python tools/fetch-battlezone-wiki-renders.py --codes bvrdev avtank

Files are written under Web/public/vehicles and accompanied by manifest.json. Existing files are
kept unless --force is supplied. The script stores source URLs for attribution and does not treat
wiki/game artwork as covered by the repository's software license.
"""

from __future__ import annotations

import argparse
import json
import mimetypes
import re
import sys
import time
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

API_URL = "https://battlezone.fandom.com/api.php"
CATEGORY = "Category:Battlezone (1998) Renders"
USER_AGENT = "BZ1-GameWatcher vehicle thumbnail importer/1.0"
PRIMARY_RENDER_RE = re.compile(
    r"^(?P<code>[A-Za-z0-9]+)[ _]+render\.(?P<ext>png|jpe?g|webp)$",
    re.IGNORECASE,
)


@dataclass(frozen=True)
class RenderFile:
    code: str
    title: str
    source_url: str
    download_url: str
    extension: str


def api_request(params: dict[str, str]) -> dict:
    query = urllib.parse.urlencode({"format": "json", "formatversion": "2", **params})
    request = urllib.request.Request(
        f"{API_URL}?{query}",
        headers={"User-Agent": USER_AGENT, "Accept": "application/json"},
    )
    with urllib.request.urlopen(request, timeout=45) as response:
        return json.load(response)


def iter_category_titles() -> Iterable[str]:
    continuation: str | None = None
    while True:
        params = {
            "action": "query",
            "list": "categorymembers",
            "cmtitle": CATEGORY,
            "cmnamespace": "6",
            "cmlimit": "max",
            "cmtype": "file",
        }
        if continuation:
            params["cmcontinue"] = continuation

        payload = api_request(params)
        for item in payload.get("query", {}).get("categorymembers", []):
            title = item.get("title")
            if isinstance(title, str):
                yield title

        continuation = payload.get("continue", {}).get("cmcontinue")
        if not continuation:
            break


def title_to_code(title: str) -> tuple[str, str] | None:
    filename = title.removeprefix("File:")
    match = PRIMARY_RENDER_RE.fullmatch(filename)
    if not match:
        return None
    return match.group("code").lower(), "." + match.group("ext").lower().replace("jpeg", "jpg")


def chunked(values: list[str], size: int) -> Iterable[list[str]]:
    for start in range(0, len(values), size):
        yield values[start:start + size]


def discover_renders(requested_codes: set[str] | None) -> list[RenderFile]:
    candidates: dict[str, tuple[str, str]] = {}
    for title in iter_category_titles():
        parsed = title_to_code(title)
        if parsed is None:
            continue
        code, extension = parsed
        if requested_codes and code not in requested_codes:
            continue
        candidates.setdefault(code, (title, extension))

    renders: list[RenderFile] = []
    titles = [title for title, _extension in candidates.values()]

    for title_batch in chunked(titles, 50):
        payload = api_request({
            "action": "query",
            "prop": "imageinfo",
            "titles": "|".join(title_batch),
            "iiprop": "url|mime|size",
            "iiurlwidth": "300",
        })

        for page in payload.get("query", {}).get("pages", []):
            title = page.get("title")
            info_items = page.get("imageinfo") or []
            if not isinstance(title, str) or not info_items:
                continue

            parsed = title_to_code(title)
            if parsed is None:
                continue
            code, fallback_extension = parsed
            info = info_items[0]
            download_url = info.get("thumburl") or info.get("url")
            if not isinstance(download_url, str):
                continue

            mime_extension = mimetypes.guess_extension(str(info.get("thumbmime") or info.get("mime") or ""))
            extension = mime_extension or fallback_extension
            if extension == ".jpe":
                extension = ".jpg"

            source_url = "https://battlezone.fandom.com/wiki/" + urllib.parse.quote(
                title.replace(" ", "_"), safe=":_()"
            )
            renders.append(RenderFile(code, title, source_url, download_url, extension.lower()))

        time.sleep(0.1)

    return sorted(renders, key=lambda render: render.code)


def download(render: RenderFile, destination: Path, force: bool) -> None:
    if destination.exists() and not force:
        return

    request = urllib.request.Request(
        render.download_url,
        headers={"User-Agent": USER_AGENT, "Referer": render.source_url},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        destination.write_bytes(response.read())


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("Web/public/vehicles"),
        help="Directory copied into the Angular public root.",
    )
    parser.add_argument(
        "--codes",
        nargs="*",
        help="Optional lowercase ODF codes to fetch; omit to fetch the full render category.",
    )
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    requested_codes = {code.lower().removesuffix(".odf") for code in args.codes or []} or None
    renders = discover_renders(requested_codes)
    args.output_dir.mkdir(parents=True, exist_ok=True)

    manifest: dict[str, dict[str, str]] = {}
    for render in renders:
        filename = f"{render.code}{render.extension}"
        destination = args.output_dir / filename
        if not args.dry_run:
            download(render, destination, args.force)
        manifest[render.code] = {
            "thumbnailUrl": f"/vehicles/{filename}",
            "sourceUrl": render.source_url,
            "originalUrl": render.download_url,
            "wikiTitle": render.title,
        }
        print(f"{render.code}: {render.title} -> {destination}")

    if not args.dry_run:
        (args.output_dir / "manifest.json").write_text(
            json.dumps(manifest, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )

    missing = sorted(requested_codes - set(manifest)) if requested_codes else []
    if missing:
        print(f"warning: no primary wiki render found for: {', '.join(missing)}", file=sys.stderr)

    print(f"Prepared {len(manifest)} render thumbnails.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Refresh the repository's self-hosted Battlezone vehicle thumbnails.

Runtime builds consume only files already committed under ``Web/public/vehicles``. This importer is
an explicit maintenance tool: it downloads reduced-size identification renders from the Battlezone
Wiki, updates ``manifest.json`` only after successful downloads, and preserves every previously
cached image when the wiki is unavailable or incomplete.

Typical usage::

    python tools/fetch-battlezone-wiki-renders.py --force
    python tools/fetch-battlezone-wiki-renders.py --codes avapc cvhraz
    python tools/fetch-battlezone-wiki-renders.py --verify-only

The default refresh is limited to ODF codes present in the generated stock catalog. Use ``--all``
only when intentionally importing every image from the configured wiki render categories.
"""

from __future__ import annotations

import argparse
import json
import mimetypes
import re
import sys
import tempfile
import time
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

API_URL = "https://battlezone.fandom.com/api.php"
CATEGORIES = (
    "Category:Battlezone (1998) Renders",
    "Category:Battlezone: The Red Odyssey Renders",
)
USER_AGENT = "BZ1-GameWatcher vehicle thumbnail importer/2.0"
PRIMARY_RENDER_RE = re.compile(
    r"^(?P<code>[A-Za-z0-9]+)[ _]+render\.(?P<ext>png|jpe?g|webp)$",
    re.IGNORECASE,
)
CATALOG_ROW_RE = re.compile(
    r'(?:(?:=\s*\[)|,)\s*\[\s*"(?P<code>[A-Za-z0-9]+)"\s*,\s*"'
)
LOCAL_THUMBNAIL_RE = re.compile(r"^/vehicles/(?P<filename>[A-Za-z0-9._-]+)$")


@dataclass(frozen=True)
class RenderCandidate:
    code: str
    title: str
    extension: str
    source_url: str | None = None


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


def iter_category_titles(category: str) -> Iterable[str]:
    continuation: str | None = None
    while True:
        params = {
            "action": "query",
            "list": "categorymembers",
            "cmtitle": category,
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
    extension = "." + match.group("ext").lower().replace("jpeg", "jpg")
    return match.group("code").lower(), extension


def chunked(values: list[str], size: int) -> Iterable[list[str]]:
    for start in range(0, len(values), size):
        yield values[start:start + size]


def load_catalog_codes(pattern: str) -> set[str]:
    paths = sorted(Path().glob(pattern))
    if not paths:
        raise FileNotFoundError(f"No generated stock catalog rows matched: {pattern}")

    codes: set[str] = set()
    for path in paths:
        codes.update(
            match.group("code").lower()
            for match in CATALOG_ROW_RE.finditer(path.read_text(encoding="utf-8"))
        )

    if not codes:
        raise ValueError(f"No ODF codes could be read from generated catalog rows: {pattern}")
    return codes


def load_overrides(path: Path) -> dict[str, RenderCandidate]:
    if not path.exists():
        return {}

    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        raise ValueError(f"Wiki override file must contain an object: {path}")

    overrides: dict[str, RenderCandidate] = {}
    for raw_code, raw_details in raw.items():
        if not isinstance(raw_details, dict):
            continue

        code = str(raw_code).strip().lower().removesuffix(".odf")
        title = raw_details.get("wikiTitle")
        source_url = raw_details.get("sourceUrl")
        if not code or not isinstance(title, str) or not title.startswith("File:"):
            print(f"warning: ignoring malformed wiki override for {raw_code!r}", file=sys.stderr)
            continue

        parsed = title_to_code(title)
        extension = parsed[1] if parsed else Path(title.removeprefix("File:")).suffix.lower()
        if extension == ".jpeg":
            extension = ".jpg"
        if extension not in {".png", ".jpg", ".webp"}:
            print(f"warning: unsupported override image extension for {code}: {extension}", file=sys.stderr)
            continue

        overrides[code] = RenderCandidate(
            code=code,
            title=title,
            extension=extension,
            source_url=source_url if isinstance(source_url, str) else None,
        )

    return overrides


def discover_renders(
    requested_codes: set[str] | None,
    overrides: dict[str, RenderCandidate],
) -> list[RenderFile]:
    candidates: dict[str, RenderCandidate] = {}
    for category in CATEGORIES:
        try:
            titles = iter_category_titles(category)
            for title in titles:
                parsed = title_to_code(title)
                if parsed is None:
                    continue
                code, extension = parsed
                if requested_codes and code not in requested_codes:
                    continue
                candidates.setdefault(code, RenderCandidate(code, title, extension))
        except Exception as exc:
            print(f"warning: failed to read {category}: {exc}", file=sys.stderr)

    for code, candidate in overrides.items():
        if requested_codes and code not in requested_codes:
            continue
        candidates[code] = candidate

    by_title = {candidate.title: candidate for candidate in candidates.values()}
    renders: list[RenderFile] = []

    for title_batch in chunked(list(by_title), 50):
        try:
            payload = api_request({
                "action": "query",
                "prop": "imageinfo",
                "titles": "|".join(title_batch),
                "iiprop": "url|mime|size",
                "iiurlwidth": "300",
            })
        except Exception as exc:
            print(
                f"warning: failed to resolve wiki image batch beginning with {title_batch[0]!r}: {exc}",
                file=sys.stderr,
            )
            continue

        for page in payload.get("query", {}).get("pages", []):
            title = page.get("title")
            info_items = page.get("imageinfo") or []
            if not isinstance(title, str) or not info_items:
                continue

            candidate = by_title.get(title)
            if candidate is None:
                continue

            info = info_items[0]
            download_url = info.get("thumburl") or info.get("url")
            if not isinstance(download_url, str):
                continue

            mime_extension = mimetypes.guess_extension(
                str(info.get("thumbmime") or info.get("mime") or "")
            )
            extension = mime_extension or candidate.extension
            if extension in {".jpe", ".jpeg"}:
                extension = ".jpg"

            file_page_url = "https://battlezone.fandom.com/wiki/" + urllib.parse.quote(
                title.replace(" ", "_"), safe=":_()"
            )
            renders.append(RenderFile(
                candidate.code,
                title,
                candidate.source_url or file_page_url,
                download_url,
                extension.lower(),
            ))

        time.sleep(0.1)

    return sorted(renders, key=lambda render: render.code)


def download(render: RenderFile, destination: Path, force: bool) -> bool:
    if destination.exists() and not force:
        return True

    request = urllib.request.Request(
        render.download_url,
        headers={"User-Agent": USER_AGENT, "Referer": render.source_url},
    )
    temporary_path: Path | None = None
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            content_type = response.headers.get_content_type()
            if not content_type.startswith("image/"):
                raise ValueError(f"unexpected content type {content_type!r}")
            payload = response.read()
            if not payload:
                raise ValueError("empty response")

        with tempfile.NamedTemporaryFile(
            mode="wb",
            prefix=f".{destination.name}.",
            suffix=".tmp",
            dir=destination.parent,
            delete=False,
        ) as temporary:
            temporary.write(payload)
            temporary_path = Path(temporary.name)

        temporary_path.replace(destination)
        return True
    except Exception as exc:
        print(f"warning: failed to download {render.code} from {render.download_url}: {exc}", file=sys.stderr)
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)
        return False


def load_existing_manifest(path: Path) -> dict[str, dict[str, object]]:
    if not path.exists():
        return {}

    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        raise ValueError(f"Existing manifest must contain an object: {path}")

    return {
        str(code).lower(): details
        for code, details in raw.items()
        if isinstance(details, dict)
    }


def validate_manifest(manifest: dict[str, dict[str, object]], output_dir: Path) -> list[str]:
    errors: list[str] = []
    for code, details in sorted(manifest.items()):
        thumbnail_url = details.get("thumbnailUrl")
        if not isinstance(thumbnail_url, str):
            errors.append(f"{code}: thumbnailUrl is missing")
            continue

        match = LOCAL_THUMBNAIL_RE.fullmatch(thumbnail_url)
        if match is None:
            errors.append(f"{code}: thumbnailUrl must be a same-origin /vehicles/ path")
            continue

        image_path = output_dir / match.group("filename")
        if not image_path.is_file() or image_path.stat().st_size == 0:
            errors.append(f"{code}: referenced image does not exist or is empty: {image_path}")

        source_url = details.get("sourceUrl")
        if not isinstance(source_url, str) or not source_url:
            errors.append(f"{code}: sourceUrl is missing")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("Web/public/vehicles"),
        help="Directory copied into the Angular public root.",
    )
    parser.add_argument(
        "--override-file",
        type=Path,
        default=Path("Web/public/vehicles/wiki-overrides.json"),
        help="Page-specific file mappings absent from the configured render categories.",
    )
    parser.add_argument(
        "--catalog-pattern",
        default="Web/src/app/data/stock-vehicles.rows-*.generated.ts",
        help="Glob used to limit a default refresh to ODF codes in the generated stock catalog.",
    )
    parser.add_argument(
        "--codes",
        nargs="*",
        help="Optional ODF codes to refresh; takes precedence over the default catalog filter.",
    )
    parser.add_argument(
        "--all",
        action="store_true",
        help="Import every matching file from the configured wiki categories.",
    )
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument(
        "--verify-only",
        action="store_true",
        help="Validate that every manifest entry points to a committed local image, without networking.",
    )
    args = parser.parse_args()

    args.output_dir.mkdir(parents=True, exist_ok=True)
    manifest_path = args.output_dir / "manifest.json"
    manifest = load_existing_manifest(manifest_path)

    if args.verify_only:
        errors = validate_manifest(manifest, args.output_dir)
        if errors:
            for error in errors:
                print(f"error: {error}", file=sys.stderr)
            return 1
        print(f"Verified {len(manifest)} committed vehicle thumbnails.")
        return 0

    if args.codes:
        requested_codes = {code.lower().removesuffix(".odf") for code in args.codes}
    elif args.all:
        requested_codes = None
    else:
        requested_codes = load_catalog_codes(args.catalog_pattern)

    overrides = load_overrides(args.override_file)
    renders = discover_renders(requested_codes, overrides)
    imported_codes: set[str] = set()

    for render in renders:
        filename = f"{render.code}{render.extension}"
        destination = args.output_dir / filename
        if args.dry_run:
            imported_codes.add(render.code)
            print(f"{render.code}: {render.title} -> {destination}")
            continue

        if not download(render, destination, args.force):
            continue

        manifest[render.code] = {
            "thumbnailUrl": f"/vehicles/{filename}",
            "sourceUrl": render.source_url,
            "originalUrl": render.download_url,
            "wikiTitle": render.title,
        }
        imported_codes.add(render.code)
        print(f"{render.code}: {render.title} -> {destination}")

    if not args.dry_run:
        manifest_path.write_text(
            json.dumps(manifest, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )

    missing = sorted(requested_codes - imported_codes) if requested_codes else []
    if missing:
        preview = ", ".join(missing[:30])
        suffix = f" ... and {len(missing) - 30} more" if len(missing) > 30 else ""
        print(f"warning: no refreshed wiki render found for: {preview}{suffix}", file=sys.stderr)

    if not args.dry_run:
        errors = validate_manifest(manifest, args.output_dir)
        if errors:
            for error in errors:
                print(f"error: {error}", file=sys.stderr)
            return 1

    print(
        f"Refreshed {len(imported_codes)} render thumbnails; "
        f"manifest contains {len(manifest)} committed entries."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

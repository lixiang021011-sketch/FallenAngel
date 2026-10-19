#!/usr/bin/env python3
"""Are the STRUM drum checkpoints actually obtainable?

Read-only on purpose. The release feed is said to carry ~6 GB, and the question
that has to be answered before any of it is downloaded is whether the two
weights this project would need are in there *as separate files* - and whether
they exist anywhere else.

    python tools/strum_availability.py

Reports, and downloads nothing.
"""

import json
import urllib.request

WANTED = (
    "drums_v13",             # the onset model: 5 second segments, streamable
    "onset_classifier_v12c", # the single drum classifier
)


def get(url: str):
    request = urllib.request.Request(url, headers={"User-Agent": "overlay-beat-probe"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def github() -> None:
    print("=== GitHub releases: opria123/strum ===")
    try:
        releases = get("https://api.github.com/repos/opria123/strum/releases")
    except Exception as error:
        print("  could not read the release feed:", error)
        return
    if not releases:
        print("  no releases at all")
        return
    for release in releases[:6]:
        assets = release.get("assets", [])
        print(f"  {release.get('tag_name')}: {len(assets)} asset(s)")
        for asset in assets:
            name = asset.get("name", "")
            size = asset.get("size", 0) / 1e9
            marks = [want for want in WANTED if want in name]
            flag = "  <-- wanted" if marks else ""
            print(f"    - {name}  {size:.2f} GB  downloads={asset.get('download_count')}{flag}")


def huggingface() -> None:
    print()
    print("=== Hugging Face: models matching 'strum' ===")
    try:
        models = get("https://huggingface.co/api/models?search=strum&limit=30")
    except Exception as error:
        print("  could not read the search API:", error)
        return
    if not models:
        print("  nothing found")
    for model in models:
        print("  " + model.get("modelId", "?"))


def main() -> int:
    print("Looking for:", ", ".join(WANTED))
    print()
    github()
    huggingface()
    print()
    print("Nothing was downloaded. This only answers whether the files exist.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

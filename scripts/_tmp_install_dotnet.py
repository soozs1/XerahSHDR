#!/usr/bin/env python3
"""Temporary helper: discover latest .NET 10 SDK and download its linux-x64 tarball."""
import json
import sys
import urllib.request

METADATA_URL = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json"
DEST = "/tmp/dotnet-sdk-10.tar.gz"


def fetch_json(url):
    req = urllib.request.Request(url, headers={"User-Agent": "xerahs-install/1.0"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode("utf-8"))


def main():
    meta = fetch_json(METADATA_URL)
    releases = meta.get("releases", [])
    # Prefer releases with an sdk, latest first (channel latest = first entry).
    candidates = []
    for rel in releases:
        sdk = rel.get("sdk") or {}
        files = sdk.get("files") or []
        for f in files:
            if f.get("rid") == "linux-x64":
                candidates.append((rel.get("sdk", {}).get("version"), rel.get("release-version"), f.get("url")))
                break
    if not candidates:
        print("NO_CANDIDATES", file=sys.stderr)
        sys.exit(2)

    # Metadata lists latest releases first; pick the first with a linux-x64 sdk.
    version, release_version, url = candidates[0]
    if not url:
        print("NO_URL", file=sys.stderr)
        sys.exit(2)

    print(f"Latest .NET 10 SDK: {version} (release {release_version})")
    print(f"Downloading: {url}")
    req = urllib.request.Request(url, headers={"User-Agent": "xerahs-install/1.0"})
    with urllib.request.urlopen(req, timeout=600) as resp, open(DEST, "wb") as out:
        total = 0
        while True:
            chunk = resp.read(1024 * 256)
            if not chunk:
                break
            out.write(chunk)
            total += len(chunk)
            if total % (16 * 1024 * 1024) == 0:
                print(f"  downloaded {total // (1024 * 1024)} MB", flush=True)
    print(f"Saved {total // (1024 * 1024)} MB to {DEST}")


if __name__ == "__main__":
    main()

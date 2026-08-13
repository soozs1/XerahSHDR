#!/usr/bin/env python3
"""Temporary helper: download latest Node.js 22.x LTS linux-x64 tarball."""
import json
import sys
import urllib.request

INDEX = "https://nodejs.org/dist/index.json"
DEST = "/tmp/node-linux-x64.tar.gz"


def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": "xerahs-install/1.0"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def main():
    data = json.loads(fetch(INDEX).decode("utf-8"))
    # Find latest stable (non-rc) v22.x.
    target = None
    for entry in data:
        v = entry.get("version", "")
        if v.startswith("v22."):
            target = entry
            break
    if not target:
        print("NO_V22", file=sys.stderr)
        sys.exit(2)

    version = target["version"]
    url = f"https://nodejs.org/dist/{version}/node-{version}-linux-x64.tar.gz"
    print(f"Node {version}: {url}")
    blob = fetch(url)
    with open(DEST, "wb") as f:
        f.write(blob)
    print(f"Saved {len(blob) // (1024 * 1024)} MB to {DEST}")


if __name__ == "__main__":
    main()

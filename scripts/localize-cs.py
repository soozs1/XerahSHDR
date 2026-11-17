#!/usr/bin/env python3
"""Batch-localize C# user-facing strings in a ported tree.

For every string literal that looks like UI text:
  * reuse an existing resource key when the value already exists (exact match);
  * otherwise create a new key ``Auto_<Sanitized>`` in the neutral resx and use it
    (Russian falls back to English until translated — that is the standard resx
    behaviour and better than hard-coded English in code).

Literals are skipped when the line looks like logging, exceptions, paths, format
strings or comparisons, so debug messages stay untouched.

Usage:
    python3 scripts/localize-cs.py <file.cs> [more.cs ...] [--dry-run]
"""
import re
import sys
import xml.etree.ElementTree as ET

RESX = "src/desktop/core/XerahS.Common/Resources.resx"
SKIP_LINE = (
    "DebugHelper", "Exception", "throw ", "nameof(", "ToString(", "ToLower", "ToUpper",
    "StartsWith", "EndsWith", "Contains(", "Replace(", "Path.", ".json", ".png", ".exe",
    "Equals(", "Split(", "Console.", "nameof", "//", "Format(", "Parse", "Join(",
)
LITERAL = re.compile(r'(?<![$@])"([A-Z][^"\\${}]{9,})"')


def load_existing():
    tree = ET.parse(RESX)
    by_value, ambiguous = {}, set()
    for data in tree.getroot().findall("data"):
        name = data.get("name")
        value_el = data.find("value")
        if not name or value_el is None or not value_el.text:
            continue
        value = value_el.text.strip()
        if value in by_value and by_value[value] != name:
            ambiguous.add(value)
        by_value[value] = name
    for value in ambiguous:
        by_value.pop(value, None)
    return by_value


def sanitize(text, limit=40):
    cleaned = re.sub(r"[^A-Za-z0-9]+", "_", text).strip("_")
    return cleaned[:limit] or "Text"


def escape(text):
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    dry_run = "--dry-run" in sys.argv
    by_value = load_existing()
    new_entries, used_keys, touched = {}, set(), 0

    for path in args:
        with open(path, encoding="utf-8-sig") as handle:
            lines = handle.readlines()

        changed = 0
        for index, line in enumerate(lines):
            if any(marker in line for marker in SKIP_LINE):
                continue
            matches = [m for m in LITERAL.finditer(line) if " " in m.group(1)]
            if not matches:
                continue

            def replace(match):
                nonlocal changed
                text = match.group(1)
                key = by_value.get(text)
                if key is None:
                    key = "Auto_" + sanitize(text)
                    suffix = 2
                    base = key
                    while key in used_keys and new_entries.get(key, text) != text:
                        key = f"{base}_{suffix}"
                        suffix += 1
                    if key not in used_keys:
                        new_entries[key] = text
                used_keys.add(key)
                changed += 1
                return f'Resources.GetString("{key}")'

            lines[index] = LITERAL.sub(replace, line)

        if changed:
            touched += 1
            print(f"  {path}: {changed} literals localized")
            if not dry_run:
                with open(path, "w", encoding="utf-8") as handle:
                    handle.writelines(lines)
        else:
            print(f"  {path}: nothing to do")

    print(f"files touched: {touched}, new keys: {len(new_entries)}")
    if not new_entries:
        return

    if dry_run:
        for key, text in list(new_entries.items())[:10]:
            print(f"    {key} = {text!r}")
        return

    with open(RESX, encoding="utf-8-sig") as handle:
        content = handle.read()
    block = "".join(
        f'  <data name="{key}" xml:space="preserve">\n    <value>{escape(text)}</value>\n  </data>\n'
        for key, text in new_entries.items()
    )
    content = content.replace("</root>", block + "</root>")
    with open(RESX, "w", encoding="utf-8") as handle:
        handle.write(content)
    print(f"added {len(new_entries)} keys to {RESX}")


if __name__ == "__main__":
    main()

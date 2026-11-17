#!/usr/bin/env python3
"""Re-apply XAML localization onto upstream files by exact string match.

The fork's translations live in ``src/desktop/core/XerahS.Common/Resources.resx``
as ``key -> English text`` pairs. Upstream v0.34.1 has the same UI with plain
English strings. This tool rewrites plain string attributes in upstream XAML
files into ``{local:Localize Key}`` **only** when the attribute value matches a
resource value exactly and that value maps to a single key.

Anything that does not match is left untouched (English), which is the safe
default: a wrong pick would silently attach the wrong translation.

Usage:
    python3 scripts/localize-xaml.py <file.axaml> [more.axaml ...]
    python3 scripts/localize-xaml.py --dry-run <file.axaml>
"""
import re
import sys
import xml.etree.ElementTree as ET

RESX = "src/desktop/core/XerahS.Common/Resources.resx"
LOCALIZE_NS = 'xmlns:local="using:XerahS.UI.Markup"'

# Attributes that render user-visible text.
TEXT_ATTRS = ("Text", "Content", "Header", "PlaceholderText", "Watermark", "ToolTip.Tip")


def load_resources():
    """value -> key, only where the value is unambiguous."""
    tree = ET.parse(RESX)
    by_value = {}
    ambiguous = set()
    for data in tree.getroot().findall("data"):
        name = data.get("name")
        value_el = data.find("value")
        if not name or value_el is None or value_el.text is None:
            continue
        value = value_el.text.strip()
        if not value or len(value) < 2:
            continue
        if value in by_value and by_value[value] != name:
            ambiguous.add(value)
        by_value[value] = name

    for value in ambiguous:
        by_value.pop(value, None)
    return by_value


def choose_prefix(text):
    """Returns (prefix, needs_namespace_declaration).

    Some upstream files already declare ``xmlns:local`` for another namespace
    (``using:XerahS.UI.Views``). Reusing that prefix would either shadow it or
    duplicate the attribute, so a distinct ``localize`` prefix is used there.
    """
    if re.search(r'xmlns:local="using:XerahS.UI.Markup"', text):
        return "local", False
    if re.search(r'xmlns:local=', text):
        return "localize", not re.search(r'xmlns:localize=', text)
    return "local", True


def ensure_namespace(text, prefix):
    declaration = f'xmlns:{prefix}="using:XerahS.UI.Markup"'
    if declaration in text:
        return text
    # Insert next to another xmlns so the root element stays valid.
    match = re.search(r'^(\s*)<(\w[\w:.]*)\s', text, re.MULTILINE)
    if not match:
        return text
    insert_at = match.end()
    indent = " " * (len(match.group(1)) + 1 + len(match.group(2)) + 1)
    return text[:insert_at] + declaration + "\n" + indent + text[insert_at:]


def rewrite(text, by_value, prefix):
    changed = 0

    def replace(match):
        nonlocal changed
        attr, value = match.group(1), match.group(2)
        key = by_value.get(value.strip())
        if key is None:
            return match.group(0)
        changed += 1
        return f'{attr}="{{{prefix}:Localize {key}}}"'

    pattern = re.compile(r'\b(' + "|".join(re.escape(a) for a in TEXT_ATTRS) + r')="([^"{}]*)"')
    return pattern.sub(replace, text), changed


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    dry_run = "--dry-run" in sys.argv
    by_value = load_resources()
    print(f"resource values usable for exact match: {len(by_value)}")

    total = 0
    for path in args:
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        prefix, needs_declaration = choose_prefix(original)
        text, changed = rewrite(original, by_value, prefix)
        if changed == 0:
            print(f"  {path}: no matches")
            continue
        if needs_declaration:
            text = ensure_namespace(text, prefix)
        if not dry_run:
            with open(path, "w", encoding="utf-8") as handle:
                handle.write(text)
        total += changed
        print(f"  {path}: {changed} strings localized with '{prefix}' prefix{' (dry run)' if dry_run else ''}")

    print(f"total localized: {total}")


if __name__ == "__main__":
    main()

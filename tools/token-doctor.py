#!/usr/bin/env python3
"""Token doctor - checks that every design token the renderer CSS references is
actually defined in tokens.css.

A `var(--nl-does-not-exist)` does not fall back to anything: the whole
declaration becomes invalid at computed-value time and the property silently
takes its initial value. A gap collapses to 0, a padding disappears, a font-size
drops to the inherited one - and nothing anywhere reports it. That is how the
extension settings form ended up rendering as an unstyled column: it referenced
--nl-space-1/2/3 and --nl-font-body-small, none of which ever existed, and only
kept working because each var() carried a hardcoded fallback.

Run from the repo root:  python tools/token-doctor.py
"""
from __future__ import annotations

import argparse
import pathlib
import re
import sys

CSS_ROOT = pathlib.Path("app/src/renderer/src")
TOKENS = CSS_ROOT / "styles" / "tokens.css"

DEFINITION = re.compile(r"^\s*(--n[lv]-[a-z0-9-]+)\s*:", re.M)
REFERENCE = re.compile(r"var\(\s*(--n[lv]-[a-z0-9-]+)")
# var(--token, fallback) - legal CSS, but on a token that does exist the
# fallback is dead weight that silently drifts from the real value.
REFERENCE_WITH_FALLBACK = re.compile(r"var\(\s*(--n[lv]-[a-z0-9-]+)\s*,")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=pathlib.Path,
        default=CSS_ROOT,
        help="renderer source root (default: app/src/renderer/src)",
    )
    parser.add_argument(
        "--allow-fallbacks",
        action="store_true",
        help="do not report var() fallbacks on tokens that are defined",
    )
    args = parser.parse_args()

    tokens_file = args.root / "styles" / "tokens.css"
    if not tokens_file.is_file():
        print(f"token-doctor: no tokens.css at {tokens_file}", file=sys.stderr)
        return 2

    defined = set(DEFINITION.findall(tokens_file.read_text(encoding="utf-8")))

    used: dict[str, set[str]] = {}
    fallbacks: dict[str, set[str]] = {}
    for path in sorted(args.root.rglob("*.css")):
        text = path.read_text(encoding="utf-8")
        for match in REFERENCE.finditer(text):
            used.setdefault(match.group(1), set()).add(path.as_posix())
        for match in REFERENCE_WITH_FALLBACK.finditer(text):
            fallbacks.setdefault(match.group(1), set()).add(path.as_posix())

    missing = {name: files for name, files in used.items() if name not in defined}
    stale = {} if args.allow_fallbacks else {n: f for n, f in fallbacks.items() if n in defined}

    print(f"{len(defined)} tokens defined, {len(used)} referenced")

    failed = False
    if missing:
        failed = True
        print("\nReferenced but never defined (the declaration is silently dropped):")
        for name in sorted(missing):
            print(f"  {name}")
            for file in sorted(missing[name]):
                print(f"      {file}")

    if stale:
        failed = True
        print("\nvar() fallback on a token that exists (drop the fallback):")
        for name in sorted(stale):
            print(f"  {name}")
            for file in sorted(stale[name]):
                print(f"      {file}")

    if failed:
        return 1

    print("every referenced token is defined, with no redundant fallbacks")
    return 0


if __name__ == "__main__":
    sys.exit(main())

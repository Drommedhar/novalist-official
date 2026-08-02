#!/usr/bin/env python3
"""Token doctor - checks that every design token the renderer references is
actually defined in tokens.css. CSS, TypeScript and TSX alike.

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

# Extensions style their panels with the same tokens and were never checked.
# A token that does not exist is a declaration the browser silently drops, so
# the panel renders with no colour and nothing says why. Sibling checkouts, so
# they are used when present and skipped in silence when they are not.
EXTENSION_WORKSPACES = [
    pathlib.Path("..") / "novalist-extension",
    pathlib.Path("..") / "novalist-aiassistant",
]


# A size written as a number rather than taken from the scale. Both of these
# are how "the sizes are all over the place" happens: one panel at 11px beside
# another at 11.5px reads as a mistake even when nobody can say which is wrong.
#
# Only px is checked. em and rem are relative to something the author chose on
# purpose - the prose size, the parent - and that is a different decision.
# A colour written out instead of taken from the theme. This is the one that
# actually breaks a theme: the two gold washes in the editor were the accent
# spelled by hand, so changing the accent left them behind.
RAW_COLOUR = re.compile(r"#[0-9a-fA-F]{3,8}\b|\brgba?\(")

RAW_FONT = re.compile(r"font-size:\s*[\d.]+px")
RAW_SPACE = re.compile(r"\b(?:padding|margin|gap|row-gap|column-gap):\s*[^;]*?\b[\d.]+px")

# Files that legitimately measure in raw pixels: the token scale itself, and
# anything drawing at a fixed device size rather than at a text size.
RAW_ALLOWED = {"tokens.css"}

SKIP_DIRS = {"bin", "obj", "node_modules", "dist", "out"}


def extension_roots() -> list[pathlib.Path]:
    """Every extension web folder beside this repo."""
    roots: list[pathlib.Path] = []
    for workspace in EXTENSION_WORKSPACES:
        if not workspace.is_dir():
            continue
        for web in sorted(workspace.rglob("web")):
            if web.is_dir() and not any(part in SKIP_DIRS for part in web.parts):
                roots.append(web)
    return roots


DEFINITION = re.compile(r"^\s*(--n[lv]-[a-z0-9-]+)\s*:", re.M)
REFERENCE = re.compile(r"var\(\s*(--n[lv]-[a-z0-9-]+)")
# var(--token, fallback) - legal CSS, but on a token that does exist the
# fallback is dead weight that silently drifts from the real value.
REFERENCE_WITH_FALLBACK = re.compile(r"var\(\s*(--n[lv]-[a-z0-9-]+)\s*,")

# TypeScript and TSX can name a token too - setProperty("--nl-accent", ...), a
# table of token names in a settings editor - and those never appear inside a
# var(). A name invented there is exactly as broken and was invisible to this
# check until a made-up --nl-surface shipped in the theme-token editor.
SCRIPT_DASHED = re.compile(r"""['"`](--n[lv]-[a-z0-9-]+)['"`]""")
# A bare "nl-something" string is only a token reference if it belongs to a
# family tokens.css actually defines. Without that test every DOM id starting
# nl- reads as a broken token: 'nl-dynamic-theme-css' is an element id and
# always was.
SCRIPT_BARE = re.compile(r"""['"`](n[lv]-[a-z0-9-]+)['"`]""")


def family(name: str) -> str:
    """The token's family: --nl-surface-card and --nl-surface-window share one."""
    parts = name.split("-")
    return "-".join(parts[:4]) if len(parts) > 3 else name


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
    families = {family(name) for name in defined}

    used: dict[str, set[str]] = {}
    fallbacks: dict[str, set[str]] = {}
    for path in sorted(args.root.rglob("*.css")):
        text = path.read_text(encoding="utf-8")
        for match in REFERENCE.finditer(text):
            used.setdefault(match.group(1), set()).add(path.as_posix())
        for match in REFERENCE_WITH_FALLBACK.finditer(text):
            fallbacks.setdefault(match.group(1), set()).add(path.as_posix())

    for pattern in ("*.ts", "*.tsx"):
        for path in sorted(args.root.rglob(pattern)):
            text = path.read_text(encoding="utf-8")
            for match in REFERENCE.finditer(text):
                used.setdefault(match.group(1), set()).add(path.as_posix())
            for match in SCRIPT_DASHED.finditer(text):
                used.setdefault(match.group(1), set()).add(path.as_posix())
            for match in SCRIPT_BARE.finditer(text):
                name = "--" + match.group(1)
                if family(name) in families:
                    used.setdefault(name, set()).add(path.as_posix())


    # The same reference scan over every extension web folder in the workspace -
    # but only for tokens that do not exist, not for fallbacks.
    #
    # A fallback is dead weight in the renderer, where :root is always right
    # there. In an extension panel it is load-bearing: the panel is a separate
    # document that inherits no tokens at all, and gets them only once the host
    # posts them in. So var(--nl-surface-card, #252526) is what a panel should
    # write - it is the colour before the theme arrives, and the colour for
    # good on a host too old to send one. Extensions ship on their own release
    # cycle, so "too old" is a real host, not a hypothetical one.
    for root in extension_roots():
        for pattern in ("*.css", "*.html", "*.js", "*.ts", "*.tsx"):
            for path in sorted(root.rglob(pattern)):
                if any(part in SKIP_DIRS for part in path.parts):
                    continue
                text = path.read_text(encoding="utf-8", errors="ignore")
                for match in REFERENCE.finditer(text):
                    used.setdefault(match.group(1), set()).add(path.as_posix())

    # Sizes written by hand instead of taken from the scale.
    raw: list[str] = []
    colours: list[str] = []
    for path in sorted(args.root.rglob("*.css")):
        if path.name in RAW_ALLOWED:
            continue
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if RAW_FONT.search(line) or RAW_SPACE.search(line):
                raw.append(f"{path.as_posix()}:{number}: {line.strip()}")
            # rgb(from var(--nl-accent) r g b / 0.05) derives from the token
            # and is exactly what this check wants people to write, so a
            # declaration that already names a token is left alone.
            if RAW_COLOUR.search(line) and 'var(--' not in line:
                colours.append(f"{path.as_posix()}:{number}: {line.strip()}")


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

    if colours:
        failed = True
        print(f"{len(colours)} colour(s) written out instead of taken from a token:")
        for entry in colours:
            print(f"  {entry}")
        print("  Use a --nl-* colour, or color-mix() over one. A literal colour")
        print("  does not follow the theme, which is the whole point of a theme.")

    if raw:
        failed = True
        print(f"{len(raw)} size(s) written in raw pixels instead of a token:")
        for entry in raw:
            print(f"  {entry}")
        print("  Use the nearest --nl-font-* or --nl-space-* step, or add a token")
        print("  to tokens.css if the scale genuinely has a gap.")

    if failed:
        return 1

    print("every referenced token is defined, with no redundant fallbacks")
    return 0


if __name__ == "__main__":
    sys.exit(main())

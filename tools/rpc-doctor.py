#!/usr/bin/env python3
"""RPC doctor - checks that the renderer and the backend agree on the JSON-RPC
surface between them.

Two directions, and only one of them can ever be a real defect:

  Unknown  - the renderer requests a method the backend does not declare. That
             call rejects at runtime with "method not found", usually as a view
             that silently fails to load. Always a bug, so it fails the build.

  Unused   - the backend declares a method nothing calls. Not automatically a
             bug (the mobile host and extensions reach the same surface, and a
             method can legitimately land before its UI), but it is how an
             orphaned feature hides: fully implemented, fully unit-tested, and
             wired to nothing. Reported, and only fatal with --strict.

This exists because line coverage cannot see the difference. The Settings
project-override switch shipped broken while Novalist.Core sat at 100%: the
Has*Override properties behind it were correct and directly unit-tested, and
nothing in production ever called them. A test calling a function is not
evidence that the app does.

Run from the repo root:  python tools/rpc-doctor.py
"""
from __future__ import annotations

import argparse
import pathlib
import re
import sys

# Backend method declarations, and every consumer that can name one.
CS_ROOTS = ["Novalist.Backend", "Novalist.Core", "Novalist.Sdk", "Novalist.Shared"]
# Novalist.Mobile hosts the same renderer bundle and also calls the backend from
# C#, so it counts as a caller when deciding whether a method is unused.
CS_CALLER_ROOTS = ["Novalist.Mobile"]
TS_ROOT = pathlib.Path("app/src")

DECLARATION = re.compile(r'JsonRpcMethod\(\s*"([^"]+)"')
# A method name is slash-separated and may carry more than two segments
# ("extensions/inlineAction/execute"), so match one-or-more separators.
NAME = r"[a-zA-Z]+(?:/[a-zA-Z]+)+"
# rpc.request<T>('a/b'), rpc.request('a/b'), rpc.notify('a/b').
TS_CALL = re.compile(rf"""(?:request|notify|invoke)\s*(?:<[^>]*>)?\s*\(\s*['"]({NAME})['"]""")
# Any bare "namespace/method" string literal. Used only to spare a method from
# the unused list - a name can reach the transport through a variable or a table.
TS_LITERAL = re.compile(rf"""['"]({NAME})['"]""")
CS_LITERAL = re.compile(rf'"({NAME})"')

SKIP_DIRS = {"obj", "bin", "node_modules", "dist", "out"}


def walk(root: pathlib.Path, suffixes: tuple[str, ...]) -> list[pathlib.Path]:
    if not root.exists():
        return []
    return [
        p
        for p in root.rglob("*")
        if p.suffix in suffixes and not SKIP_DIRS & set(p.parts)
    ]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--strict",
        action="store_true",
        help="also fail when the backend declares a method nothing calls",
    )
    args = parser.parse_args()

    declared: dict[str, str] = {}
    for root in CS_ROOTS:
        for path in walk(pathlib.Path(root), (".cs",)):
            for name in DECLARATION.findall(path.read_text(encoding="utf-8", errors="ignore")):
                declared[name] = path.as_posix()

    if not declared:
        print("rpc-doctor: no JsonRpcMethod declarations found", file=sys.stderr)
        return 2

    called: dict[str, set[str]] = {}
    named: set[str] = set()
    for path in walk(TS_ROOT, (".ts", ".tsx")):
        text = path.read_text(encoding="utf-8", errors="ignore")
        for name in TS_CALL.findall(text):
            called.setdefault(name, set()).add(path.as_posix())
        named.update(TS_LITERAL.findall(text))
    for root in CS_CALLER_ROOTS:
        for path in walk(pathlib.Path(root), (".cs",)):
            named.update(CS_LITERAL.findall(path.read_text(encoding="utf-8", errors="ignore")))

    unknown = {n: f for n, f in called.items() if n not in declared}
    unused = sorted(n for n in declared if n not in named)

    print(f"{len(declared)} backend methods declared, {len(called)} called by the renderer")

    failed = False
    if unknown:
        failed = True
        print("\nCalled but never declared (rejects at runtime as method-not-found):")
        for name in sorted(unknown):
            print(f"  {name}")
            for file in sorted(unknown[name]):
                print(f"      {file}")

    if unused:
        print("\nDeclared but nothing calls it (dead surface, or a feature wired to nothing):")
        for name in unused:
            print(f"  {name:<38} {declared[name]}")
        if args.strict:
            failed = True

    if failed:
        return 1

    print("\nevery renderer call resolves to a declared backend method")
    return 0


if __name__ == "__main__":
    sys.exit(main())

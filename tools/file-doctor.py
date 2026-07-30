#!/usr/bin/env python3
"""Catches a file that has been emptied or gutted before it can be committed.

This exists because shell.css was emptied in the working tree - all 3862 lines
- and the app was built and run from it. The whole interface rendered as
unstyled text. Nothing caught it: an empty file is valid CSS, the TypeScript
still compiled, every other doctor passed, the build succeeded, and the app
launched. A screenshot was the only thing that showed it.

It was never committed, which is the only reason it was recoverable in one
command. This check runs against HEAD so it fires while the damage is still in
the working tree, which is where it is cheap to undo.

The cause was a script that opened the file for writing and read it inside the
same expression:

    open(path, 'w').write(open(path).read())

Python evaluates the destination first, which truncates it, and the read then
returns nothing. The same shape had already destroyed a C# file earlier the
same day.

Two checks, both against HEAD so they run before a commit rather than after:

  empty    - a tracked source file with no content at all.
  gutted   - a tracked source file that has lost most of its lines.

A file legitimately shrinks - a feature comes out, a section moves elsewhere -
so the threshold is deliberately far past ordinary editing. Deleting a file is
not flagged: that is a decision git records plainly.

Run from the repo root:  python tools/file-doctor.py
"""
from __future__ import annotations

import subprocess
import sys

# Text that is code or content. A .json locale can legitimately be {} in a new
# extension, so data files are checked for emptiness only.
SOURCE_SUFFIXES = (".cs", ".ts", ".tsx", ".css", ".html", ".py", ".md", ".axaml")

# A file that keeps less than this share of its lines has almost certainly been
# truncated rather than edited. Ordinary work does not remove nine tenths of a
# file and leave the rest.
KEPT_THRESHOLD = 0.10

# Files small enough that a big proportional drop means nothing.
MIN_LINES = 40


def git(*args: str) -> str:
    return subprocess.run(
        ["git", *args], capture_output=True, text=True, encoding="utf-8", errors="ignore"
    ).stdout


def main() -> int:
    tracked = [p for p in git("ls-files").splitlines() if p.strip()]
    empty: list[str] = []
    gutted: list[tuple[str, int, int]] = []

    for path in tracked:
        if not path.endswith(SOURCE_SUFFIXES):
            continue
        try:
            with open(path, encoding="utf-8", errors="ignore") as handle:
                now = handle.read()
        except FileNotFoundError:
            # Deleted on purpose; git will show that on its own.
            continue

        if now.strip() == "":
            empty.append(path)
            continue

        was = git("show", f"HEAD:{path}")
        if not was:
            continue  # new file, nothing to compare against

        before = was.count("\n")
        after = now.count("\n")
        if before >= MIN_LINES and after < before * KEPT_THRESHOLD:
            gutted.append((path, before, after))

    if empty:
        print("Tracked source files that are empty:")
        for path in empty:
            print(f"  {path}")

    if gutted:
        print("Tracked source files that have lost almost everything:")
        for path, before, after in gutted:
            print(f"  {path}: {before} lines -> {after}")

    if empty or gutted:
        print()
        print("  If this is deliberate, delete the file rather than emptying it, or")
        print("  commit the removal on its own so it reads as a decision.")
        print("  If it is not, restore it:  git checkout HEAD -- <path>")
        return 1

    print("no tracked source file is empty or gutted")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Read and stamp CHANGELOG.md.

Two jobs, both used by .github/workflows/release.yml:

  extract  print one release section's body - the release workflow feeds this to
           the GitHub release as its notes.

             python tools/changelog.py extract --version 2.1.1
             python tools/changelog.py extract --unreleased

  release  turn the "Unreleased" heading into a real release heading for the tag
           that was just pushed, open a fresh empty Unreleased section above it,
           and fix up the compare links at the bottom of the file.

             python tools/changelog.py release --version 2.2.0 --date 2026-08-01 \
                 --tag v2.2.0 --previous-tag v2.1.1

Headings look like `## [Unreleased]` or `## [2.1.1] - 2026-07-21`; a section runs
until the next `## ` heading, with the trailing `---` separator and blank lines
trimmed off. The repository URL is read from the existing compare links, so this
script has nothing hardcoded about where the project lives.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

DEFAULT_FILE = Path(__file__).resolve().parent.parent / "CHANGELOG.md"

HEADING = re.compile(r"^## \[(?P<name>[^\]]+)\]")
LINK = re.compile(r"^\[(?P<name>[^\]]+)\]:\s*(?P<url>\S+)\s*$")

EMPTY_UNRELEASED = "## [Unreleased]\n\nNothing yet.\n\n---\n\n"


def read(path: Path) -> list[str]:
    if not path.is_file():
        sys.exit(f"changelog: {path} not found")
    return path.read_text(encoding="utf-8").splitlines(keepends=True)


def find_section(lines: list[str], name: str) -> tuple[int, int]:
    """Return [start, end) line indices of the section whose heading is `name`.

    Matching ignores a leading `v` on either side, so `v2.1.1` finds `[2.1.1]`.
    """
    want = name.lstrip("vV")
    start = None
    for i, line in enumerate(lines):
        # The link-definition block at the foot of the file ends the last section.
        if start is not None and LINK.match(line):
            return start, i
        m = HEADING.match(line)
        if not m:
            continue
        if start is not None:
            return start, i
        if m.group("name").lstrip("vV") == want:
            start = i
    if start is None:
        raise KeyError(name)
    return start, len(lines)


def body(lines: list[str], start: int, end: int) -> str:
    """The section without its heading, trailing `---` rule, and blank lines."""
    out = lines[start + 1 : end]
    while out and (out[-1].strip() == "" or out[-1].strip() == "---"):
        out.pop()
    while out and out[0].strip() == "":
        out.pop(0)
    return "".join(out)


def cmd_extract(args: argparse.Namespace) -> int:
    lines = read(args.file)
    name = "Unreleased" if args.unreleased else args.version
    try:
        start, end = find_section(lines, name)
    except KeyError:
        sys.exit(f"changelog: no section for '{name}' in {args.file}")
    text = body(lines, start, end)
    if not text.strip() and args.require_content:
        sys.exit(f"changelog: section '{name}' is empty")
    sys.stdout.write(text)
    return 0


def repo_url(lines: list[str]) -> str | None:
    """Derive `https://host/owner/repo` from any compare/tag link in the file."""
    for line in lines:
        m = LINK.match(line)
        if not m:
            continue
        url = m.group("url")
        for marker in ("/compare/", "/releases/tag/"):
            if marker in url:
                return url.split(marker)[0]
    return None


def rewrite_links(lines: list[str], version: str, tag: str, previous_tag: str | None) -> None:
    """Repoint [Unreleased] at the new tag and add a link for the new version."""
    base = repo_url(lines)
    if base is None:
        print("changelog: no compare links found, skipping link update", file=sys.stderr)
        return
    for i, line in enumerate(lines):
        m = LINK.match(line)
        if not m or m.group("name") != "Unreleased":
            continue
        if previous_tag:
            new_link = f"[{version}]: {base}/compare/{previous_tag}...{tag}\n"
        else:
            new_link = f"[{version}]: {base}/releases/tag/{tag}\n"
        lines[i : i + 1] = [f"[Unreleased]: {base}/compare/{tag}...HEAD\n", new_link]
        return
    print("changelog: no [Unreleased] link found, skipping link update", file=sys.stderr)


def cmd_release(args: argparse.Namespace) -> int:
    lines = read(args.file)
    try:
        start, _ = find_section(lines, "Unreleased")
    except KeyError:
        sys.exit(f"changelog: no Unreleased section in {args.file}")

    try:
        find_section(lines, args.version)
        sys.exit(f"changelog: {args.version} is already released in {args.file}")
    except KeyError:
        pass

    lines[start] = f"## [{args.version}] - {args.date}\n"
    lines.insert(start, EMPTY_UNRELEASED)
    rewrite_links(lines, args.version, args.tag, args.previous_tag)

    args.file.write_text("".join(lines), encoding="utf-8")
    print(f"changelog: stamped Unreleased as {args.version} ({args.date})")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Read and stamp CHANGELOG.md")
    parser.add_argument("--file", type=Path, default=DEFAULT_FILE, help="path to CHANGELOG.md")
    sub = parser.add_subparsers(dest="command", required=True)

    ex = sub.add_parser("extract", help="print a release section's body")
    group = ex.add_mutually_exclusive_group(required=True)
    group.add_argument("--version", help="version or tag to extract (v-prefix optional)")
    group.add_argument("--unreleased", action="store_true", help="extract the Unreleased section")
    ex.add_argument(
        "--require-content",
        action="store_true",
        help="fail instead of printing nothing when the section is empty",
    )
    ex.set_defaults(func=cmd_extract)

    rel = sub.add_parser("release", help="stamp Unreleased with a version and date")
    rel.add_argument("--version", required=True, help="version being released, e.g. 2.2.0")
    rel.add_argument("--date", required=True, help="release date, YYYY-MM-DD")
    rel.add_argument("--tag", required=True, help="git tag being released, e.g. v2.2.0")
    rel.add_argument("--previous-tag", help="previous tag, used for the compare link")
    rel.set_defaults(func=cmd_release)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())

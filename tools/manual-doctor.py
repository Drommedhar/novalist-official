#!/usr/bin/env python3
"""Validate the bundled user manual before broken help ships.

Checks local Markdown targets, heading fragments, images, and README table-of-
contents coverage. Code examples are deliberately ignored: documentation often
shows literal Markdown link syntax that is not itself a link.

Run from the repository root:
    python tools/manual-doctor.py
"""
from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import unquote

REPO_ROOT = Path(__file__).resolve().parent.parent
FENCE_RE = re.compile(r"^\s*((?:\x60){3,}|~{3,})")
HEADING_RE = re.compile(r"^(#{1,6})\s+(.+?)\s*#*\s*$")
INLINE_CODE_RE = re.compile(r"((?:\x60)+).*?\1")
LINK_RE = re.compile(
    r"(?P<image>!)?\[(?P<label>[^\]]*)\]"
    r"\(\s*(?P<target><[^>]+>|[^\s)]+)(?:\s+[^)]*)?\)"
)
EXTERNAL_RE = re.compile(r"^[a-z][a-z0-9+.-]*:", re.IGNORECASE)


@dataclass(frozen=True, order=True)
class Problem:
    path: Path
    line: int
    message: str

    def render(self, root: Path) -> str:
        try:
            shown = self.path.relative_to(root)
        except ValueError:
            shown = self.path
        return f"{shown}:{self.line}: {self.message}"


@dataclass(frozen=True)
class MarkdownLink:
    line: int
    target: str
    image: bool


def visible_lines(text: str) -> list[str]:
    """Keep line numbers stable while blanking fenced and inline code."""
    result: list[str] = []
    fence: str | None = None
    for line in text.splitlines():
        match = FENCE_RE.match(line)
        if match:
            marker = match.group(1)[0]
            fence = marker if fence is None else None if fence == marker else fence
            result.append("")
            continue
        if fence is not None:
            result.append("")
            continue
        result.append(INLINE_CODE_RE.sub("", line))
    return result


def markdown_links(text: str) -> list[MarkdownLink]:
    links: list[MarkdownLink] = []
    for line_number, line in enumerate(visible_lines(text), start=1):
        for match in LINK_RE.finditer(line):
            target = match.group("target")
            if target.startswith("<") and target.endswith(">"):
                target = target[1:-1]
            links.append(
                MarkdownLink(
                    line=line_number,
                    target=target,
                    image=match.group("image") is not None,
                )
            )
    return links


def slugify_heading(value: str) -> str:
    value = re.sub(r"\[([^\]]+)]\([^)]+\)", r"\1", value.strip().lower())
    value = re.sub(r"<[^>]*>", "", value)
    value = re.sub(r"[\x60*_~]", "", value)
    value = "".join(char for char in value if char.isalnum() or char.isspace() or char == "-")
    return re.sub(r"\s", "-", value)


def heading_anchors(text: str) -> set[str]:
    anchors: set[str] = set()
    occurrences: dict[str, int] = {}
    for line in visible_lines(text):
        match = HEADING_RE.match(line)
        if not match:
            continue
        base = slugify_heading(match.group(2))
        seen = occurrences.get(base, 0)
        occurrences[base] = seen + 1
        anchors.add(base if seen == 0 else f"{base}-{seen}")
    return anchors


def split_target(target: str) -> tuple[str, str]:
    before_hash, separator, fragment = target.partition("#")
    path = before_hash.partition("?")[0]
    return unquote(path), unquote(fragment) if separator else ""


def is_external(target: str) -> bool:
    return target.startswith("//") or EXTERNAL_RE.match(target) is not None


def check_manual(manual_dir: Path) -> list[Problem]:
    manual_dir = manual_dir.resolve()
    pages = sorted(manual_dir.glob("*.md"))
    problems: list[Problem] = []
    texts: dict[Path, str] = {}
    anchors: dict[Path, set[str]] = {}

    for page in pages:
        text = page.read_text(encoding="utf-8-sig")
        resolved = page.resolve()
        texts[resolved] = text
        anchors[resolved] = heading_anchors(text)

    for page in pages:
        source = page.resolve()
        for link in markdown_links(texts[source]):
            if is_external(link.target):
                continue
            path_part, fragment = split_target(link.target)
            destination = (page.parent / path_part).resolve() if path_part else source

            if link.image:
                if not path_part or not destination.is_file():
                    problems.append(
                        Problem(page, link.line, f"missing image: {link.target}")
                    )
                continue

            if path_part and destination.suffix.lower() != ".md":
                if not destination.is_file():
                    problems.append(
                        Problem(page, link.line, f"missing local target: {link.target}")
                    )
                continue

            if destination not in texts:
                problems.append(
                    Problem(page, link.line, f"missing manual page: {path_part or link.target}")
                )
                continue

            if fragment and fragment not in anchors[destination]:
                problems.append(
                    Problem(
                        page,
                        link.line,
                        f"missing heading '#{fragment}' in {destination.name}",
                    )
                )

    index = (manual_dir / "README.md").resolve()
    if index not in texts:
        problems.append(Problem(manual_dir / "README.md", 1, "manual index is missing"))
    else:
        indexed: set[Path] = set()
        for link in markdown_links(texts[index]):
            if link.image or is_external(link.target):
                continue
            path_part, _ = split_target(link.target)
            if path_part.lower().endswith(".md"):
                indexed.add(((manual_dir / path_part).resolve()))
        for page in pages:
            if page.resolve() != index and page.resolve() not in indexed:
                problems.append(
                    Problem(page, 1, "page is missing from README.md table of contents")
                )

    return sorted(problems)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=REPO_ROOT,
        help="repository root containing docs/manual (default: this checkout)",
    )
    args = parser.parse_args(argv)
    root = args.root.resolve()
    manual_dir = root / "docs" / "manual"
    if not manual_dir.is_dir():
        print(f"manual directory not found: {manual_dir}", file=sys.stderr)
        return 2

    problems = check_manual(manual_dir)
    if problems:
        print("manual validation failed:")
        for problem in problems:
            print(f"  {problem.render(root)}")
        return 1

    print(f"manual links, anchors, images, and TOC are valid ({len(list(manual_dir.glob('*.md')))} pages)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

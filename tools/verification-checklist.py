#!/usr/bin/env python3
"""Turn the competitor audit into a checklist you can actually work through.

docs/plans/competitor-feature-audit.md records 284 findings in three different
layouts - P0 as `###` prose, P1 as `####` prose under an area heading, P2 and P3
as table rows - and every shipped row carries a `TEST:` route saying how to
check it. That is a complete manual test plan, and it is unusable in that form:
the routes are scattered across two thousand lines in reading order, so
verifying the Codex means finding twenty-four rows filed under twenty-four
different headings.

This regroups them by the screen you have to open, so checking the Codex is one
sitting rather than twenty-four errands, and writes a self-contained HTML page
that remembers what you have ticked.

Run from the repo root:  python tools/verification-checklist.py
"""

from __future__ import annotations

import argparse
import html
import json
import pathlib
import re
import unicodedata
from collections import Counter, OrderedDict

DOC = pathlib.Path("docs/plans/competitor-feature-audit.md")
OUT = pathlib.Path("docs/plans/verification-checklist.html")

PLACEMENTS = ("core", "extension-after-sdk-work", "extension", "hybrid")

# Which screen a route sends you to. First match wins, so the specific ones
# come before the general - "Plot Grid" before "Grid", "Start screen" before
# "screen". A row matching nothing lands in "Elsewhere", which is a real
# answer: some routes name a service rather than a place.
SURFACES: list[tuple[str, str]] = [
    ("Start screen", r"start screen|welcome screen|recent projects"),
    ("Binder", r"\bbinder\b"),
    ("Editor", r"\beditor\b|\bcompose mode\b|typewriter|caret|context menu"),
    ("Inspector", r"\binspector\b"),
    (
        "Manuscript, Corkboard and Outliner",
        r"manuscript view|corkboard|outliner|\bboard\b",
    ),
    ("Codex", r"\bcodex\b|entity|character sheet"),
    ("Wiki", r"\bwiki\b"),
    ("Timeline", r"\btimeline\b"),
    ("Calendar", r"\bcalendar\b"),
    ("Plot Grid", r"plot ?grid|plotline|plot lane"),
    ("Relationships", r"relationship"),
    ("Maps", r"\bmaps?\b(?! pins? only)"),
    ("Research and Library", r"research|library|gallery|scratchpad|darlings"),
    ("Dashboard and analytics", r"dashboard|analytics|statistics|goal"),
    ("Style report", r"style view|style report|prose style|readability"),
    ("Planning canvas", r"\bcanvas\b"),
    ("Series", r"\bseries\b"),
    ("Export", r"\bexport\b|compile|epub|docx|pdf|latex|final draft|normseiten|shunn"),
    ("Import", r"\bimport\b|\bscriv\b"),
    ("Settings", r"\bsettings\b|preferences"),
    ("Extensions", r"extension|plugin|sdk"),
    ("Git and snapshots", r"\bgit\b|snapshot|backup|version history"),
    ("Dialogue", r"dialogue"),
    ("Exposé", r"expos"),
    ("Command palette and hotkeys", r"command palette|ctrl\+|hotkey|shortcut"),
    ("Status bar", r"status bar|sprint|pomodoro"),
]


def slug(text: str) -> str:
    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")[:80]


def surface_of(row: dict) -> str:
    hay = f"{row['route']} {row['name']}"
    for name, pattern in SURFACES:
        if re.search(pattern, hay, re.IGNORECASE):
            return name
    return "Elsewhere"


def tidy(text: str) -> str:
    return " ".join(text.split())


def parse_prose_sections(text: str, p0: int, p1: int, p2: int) -> list[dict]:
    rows: list[dict] = []

    def meta(chunk: str) -> tuple[str, str]:
        line = next((l for l in chunk.split("\n")[1:6] if l.startswith("`")), "")
        tags = re.findall(r"`([^`]+)`", line)
        pri = next((t for t in tags if re.fullmatch(r"P\d", t)), "")
        return pri, next((t for t in tags if t in PLACEMENTS), "")

    def prose(chunk: str) -> tuple[str, str, str]:
        """route, why, competitors - from a `###`/`####` finding body."""
        m = re.search(r"TEST:\s*(.+?)(?:\n\n|\n-|$)", chunk, re.DOTALL)
        route = tidy(m.group(1)) if m else ""
        body = chunk[m.end() :] if m else chunk
        why = tidy(body.split("\n- Competitors:")[0])
        comp = re.search(r"\n- Competitors:\s*(.+)", chunk)
        return route, why, tidy(comp.group(1)) if comp else ""

    # P0: ### sections
    for chunk in re.split(r"\n(?=### )", text[p0:p1]):
        if not chunk.lstrip().startswith("### "):
            continue
        name = chunk.lstrip()[4:].split("\n", 1)[0].strip()
        pri, placement = meta(chunk)
        route, why, comp = prose(chunk)
        rows.append(
            {
                "name": name,
                "pri": pri or "P0",
                "placement": placement,
                "done": "**DONE.**" in chunk,
                "route": route,
                "why": why,
                "competitors": comp,
            }
        )

    # P1: #### sections
    for chunk in re.split(r"\n(?=#### |### )", text[p1:p2]):
        if not chunk.lstrip().startswith("#### "):
            continue
        name = chunk.lstrip()[5:].split("\n", 1)[0].strip()
        _, placement = meta(chunk)
        route, why, comp = prose(chunk)
        rows.append(
            {
                "name": name,
                "pri": "P1",
                "placement": placement,
                "done": "**DONE.**" in chunk,
                "route": route,
                "why": why,
                "competitors": comp,
            }
        )

    return rows


def parse() -> tuple[list[dict], list[str]]:
    # docs/plans is gitignored - the audit is local working material - so a
    # fresh checkout has this tool and not its input. Say which file is
    # missing rather than raising a traceback about it.
    if not DOC.is_file():
        raise SystemExit(f"verification-checklist: no audit at {DOC.as_posix()}")
    text = DOC.read_text(encoding="utf-8")
    p0, p1, p2 = (
        text.index(m)
        for m in (
            "## P0 findings",
            "## P1 findings by area",
            "## P2 and P3 findings by area",
        )
    )
    rows: list[dict] = []

    rows.extend(parse_prose_sections(text, p0, p1, p2))

    # P2 and P3: table rows
    for line in text[p2:].split("\n"):
        if not line.startswith("| ") or line.startswith(("|---", "| Feature")):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split(" | ")]
        if len(cells) < 7:
            continue
        name, pri, status, _effort, placement, detail = cells[:6]
        route, why = "", tidy(detail)
        m = re.search(r"TEST:\s*(.*)", detail)
        if m:
            # The route is the first sentence; the gap description follows it.
            # An imperfect split loses nothing, because both halves are shown.
            head, _, tail = m.group(1).partition(". ")
            route, why = tidy(head), tidy(tail)
        rows.append(
            {
                "name": name,
                "pri": pri,
                "placement": placement,
                "done": "Done" in status or "DONE" in status,
                "route": route,
                "why": why,
                "competitors": tidy(cells[6]),
            }
        )

    # The rows closed by decision rather than by code, so the totals reconcile.
    decisions = re.findall(
        r"^- ([A-Z].+?)\.",
        text[
            text.index("## Decisions taken on the remaining P2 rows") : text.index(
                "## P0 findings"
            )
        ],
        re.MULTILINE,
    )
    return rows, [tidy(d) for d in decisions]


def render(rows: list[dict], decisions: list[str]) -> str:
    todo = [r for r in rows if r["done"] and r["route"]]
    groups: OrderedDict[str, list[dict]] = OrderedDict()
    for name, _ in SURFACES:
        groups[name] = []
    groups["Elsewhere"] = []
    for row in todo:
        groups[surface_of(row)].append(row)
    for name in list(groups):
        if not groups[name]:
            del groups[name]
        else:
            groups[name].sort(key=lambda r: (r["pri"], r["name"]))

    pri_counts = Counter(r["pri"] for r in todo)
    parts: list[str] = []
    # What a bug report needs to be actionable without the reader opening this
    # page: which feature, how important, which screen, and the route that was
    # supposed to work.
    meta: dict[str, dict] = {}
    for group, items in groups.items():
        rows_html = []
        for row in items:
            rid = f"{row['pri']}-{slug(row['name'])}"
            meta[rid] = {
                "name": row["name"],
                "pri": row["pri"],
                "group": group,
                "route": row["route"],
            }
            why = html.escape(row["why"]) or "No further detail recorded in the audit."
            comp = (
                f'<p class="comp"><span>Competitors that have it:</span> '
                f"{html.escape(row['competitors'])}</p>"
                if row["competitors"]
                else ""
            )
            aria = html.escape(row["name"]).replace('"', "&quot;")
            rows_html.append(f"""
        <li class="row" data-pri="{row["pri"]}" id="row-{rid}">
          <input type="checkbox" class="tick" data-id="{rid}"
                 aria-label="Verified: {aria}">
          <div class="body">
            <p class="title">
              <span class="pri p{row["pri"][1]}">{row["pri"]}</span>
              {html.escape(row["name"])}
              <span class="where">{html.escape(row["placement"])}</span>
            </p>
            <p class="route"><span>Do this:</span> {html.escape(row["route"])}</p>
            <details><summary>Why this row exists</summary>
              <p class="why">{why}</p>{comp}
            </details>
            <div class="note">
              <button class="note-toggle" data-note-for="{rid}">Add a bug note</button>
              <textarea class="note-field hidden" data-note="{rid}" rows="2"
                        aria-label="Bug note: {aria}"
                        placeholder="What is wrong, and what did you expect instead?"></textarea>
            </div>
            <p class="stamp" data-stamp="{rid}"></p>
          </div>
        </li>""")
        parts.append(f"""
      <section class="group" data-group="{html.escape(group)}">
        <h2>{html.escape(group)} <span class="count"><span class="gdone">0</span>/{len(items)}</span></h2>
        <ol class="rows">{"".join(rows_html)}</ol>
      </section>""")

    closed = "".join(f"<li>{html.escape(d)}</li>" for d in decisions)
    return TEMPLATE.format(
        total=len(todo),
        p0=pri_counts.get("P0", 0),
        p1=pri_counts.get("P1", 0),
        p2=pri_counts.get("P2", 0),
        groups="".join(parts),
        decisions=closed,
        generated=DOC.as_posix(),
        rowmeta=json.dumps(meta, ensure_ascii=False),
    )


TEMPLATE = (
    pathlib.Path(__file__).parent / "templates" / "verification-checklist.html"
).read_text(encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default=str(OUT))
    args = parser.parse_args()

    rows, decisions = parse()
    page = render(rows, decisions)
    out = pathlib.Path(args.out)
    out.write_text(page, encoding="utf-8", newline="\n")

    todo = [r for r in rows if r["done"] and r["route"]]
    missing = [r for r in rows if r["done"] and not r["route"]]
    print(
        f"{len(rows)} findings, {sum(1 for r in rows if r['done'])} shipped, "
        f"{len(todo)} with a test route"
    )
    if missing:
        print(
            f"\n{len(missing)} shipped rows carry no TEST route and are not on the list:"
        )
        for row in missing:
            print(f"  [{row['pri']}] {row['name']}")
    print(f"\nwrote {out.as_posix()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

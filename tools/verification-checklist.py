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
import io
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
    ("Manuscript, Corkboard and Outliner", r"manuscript view|corkboard|outliner|\bboard\b"),
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
        if re.search(pattern, hay, re.I):
            return name
    return "Elsewhere"


def tidy(text: str) -> str:
    return " ".join(text.split())


def parse() -> tuple[list[dict], list[str]]:
    # docs/plans is gitignored - the audit is local working material - so a
    # fresh checkout has this tool and not its input. Say which file is
    # missing rather than raising a traceback about it.
    if not DOC.is_file():
        raise SystemExit(f"verification-checklist: no audit at {DOC.as_posix()}")
    text = DOC.read_text(encoding="utf-8")
    p0, p1, p2 = (text.index(m) for m in (
        "## P0 findings", "## P1 findings by area", "## P2 and P3 findings by area"))
    rows: list[dict] = []

    def meta(chunk: str) -> tuple[str, str]:
        line = next((l for l in chunk.split("\n")[1:6] if l.startswith("`")), "")
        tags = re.findall(r"`([^`]+)`", line)
        pri = next((t for t in tags if re.fullmatch(r"P\d", t)), "")
        return pri, next((t for t in tags if t in PLACEMENTS), "")

    def prose(chunk: str) -> tuple[str, str, str]:
        """route, why, competitors - from a `###`/`####` finding body."""
        m = re.search(r"TEST:\s*(.+?)(?:\n\n|\n-|$)", chunk, re.S)
        route = tidy(m.group(1)) if m else ""
        body = chunk[m.end():] if m else chunk
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
        rows.append(dict(name=name, pri=pri or "P0", placement=placement,
                         done="**DONE.**" in chunk, route=route, why=why, competitors=comp))

    # P1: #### sections
    for chunk in re.split(r"\n(?=#### |### )", text[p1:p2]):
        if not chunk.lstrip().startswith("#### "):
            continue
        name = chunk.lstrip()[5:].split("\n", 1)[0].strip()
        _, placement = meta(chunk)
        route, why, comp = prose(chunk)
        rows.append(dict(name=name, pri="P1", placement=placement,
                         done="**DONE.**" in chunk, route=route, why=why, competitors=comp))

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
        rows.append(dict(name=name, pri=pri, placement=placement,
                         done="Done" in status or "DONE" in status,
                         route=route, why=why, competitors=tidy(cells[6])))

    # The rows closed by decision rather than by code, so the totals reconcile.
    decisions = re.findall(r"^- ([A-Z].+?)\.", text[text.index(
        "## Decisions taken on the remaining P2 rows"):text.index("## P0 findings")], re.M)
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
            meta[rid] = {"name": row["name"], "pri": row["pri"],
                         "group": group, "route": row["route"]}
            why = html.escape(row["why"]) or "No further detail recorded in the audit."
            comp = (f'<p class="comp"><span>Competitors that have it:</span> '
                    f'{html.escape(row["competitors"])}</p>' if row["competitors"] else "")
            aria = html.escape(row["name"]).replace('"', "&quot;")
            rows_html.append(f"""
        <li class="row" data-pri="{row['pri']}" id="row-{rid}">
          <input type="checkbox" class="tick" data-id="{rid}"
                 aria-label="Verified: {aria}">
          <div class="body">
            <p class="title">
              <span class="pri p{row['pri'][1]}">{row['pri']}</span>
              {html.escape(row['name'])}
              <span class="where">{html.escape(row['placement'])}</span>
            </p>
            <p class="route"><span>Do this:</span> {html.escape(row['route'])}</p>
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
        <ol class="rows">{''.join(rows_html)}</ol>
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


TEMPLATE = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Novalist verification checklist</title>
<style>
  :root {{
    --ground: #0f1219; --panel: #171c2b; --text: #ece5d2; --muted: #a8a193;
    --faint: #756f62; --gold: #d2a74f; --gold-light: #ecc97c; --on-gold: #171206;
    --line: rgb(237 230 211 / 0.08); --border: rgb(237 230 211 / 0.14);
    --ok: #7bae7f; --bad: #d4635a; --serif: Georgia, 'Times New Roman', serif;
  }}
  * {{ box-sizing: border-box; }}
  body {{ margin: 0; background: var(--ground); color: var(--text);
         font: 15px/1.6 var(--serif);
         /* Georgia defaults to old-style figures, which draw zero as a small o -
            so "P0" read as "Po" and "202" as "2o2" on a page that is mostly
            counts. */
         font-variant-numeric: lining-nums; }}
  header {{ position: sticky; top: 0; z-index: 5; background: var(--ground);
            border-bottom: 1px solid var(--border); padding: 18px 24px 14px; }}
  h1 {{ margin: 0 0 4px; font-size: 26px; }}
  .sub {{ margin: 0 0 14px; color: var(--muted); font-size: 14px; }}
  .bar {{ height: 6px; background: var(--panel); border-radius: 99px; overflow: hidden;
          margin-bottom: 12px; }}
  .bar > i {{ display: block; height: 100%; width: 0;
              background: linear-gradient(180deg, var(--gold-light), var(--gold));
              transition: width .2s ease; }}
  .tools {{ display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }}
  button, select, input[type=search] {{ font: inherit; font-size: 14px; color: var(--text);
    background: var(--panel); border: 1px solid var(--border); border-radius: 8px;
    padding: 6px 10px; cursor: pointer; }}
  input[type=search] {{ cursor: text; min-width: 220px; }}
  button:hover, select:hover {{ border-color: var(--gold); }}
  button.on {{ background: linear-gradient(180deg, var(--gold-light), var(--gold));
               color: var(--on-gold); border-color: transparent; }}
  .spacer {{ flex: 1; }}
  main {{ padding: 8px 24px 80px; max-width: 1100px; }}
  .group {{ margin-top: 26px; }}
  .group h2 {{ font-size: 19px; margin: 0 0 8px; padding-bottom: 6px;
               border-bottom: 1px solid var(--line); }}
  .count {{ color: var(--faint); font-size: 13px; font-family: ui-monospace, monospace; }}
  ol.rows {{ list-style: none; margin: 0; padding: 0; }}
  .row {{ display: flex; gap: 12px; padding: 12px 10px; border-bottom: 1px solid var(--line);
          align-items: flex-start; }}
  .row.done .title {{ color: var(--faint); text-decoration: line-through; }}
  .row.done {{ opacity: .62; }}
  /* A real control rather than a hidden input behind a span: it stays
     reachable by keyboard and by anything driving the page. */
  .tick {{ appearance: none; flex: 0 0 auto; width: 20px; height: 20px; margin: 3px 0 0;
           border-radius: 5px; border: 1px solid var(--border); background: var(--panel);
           cursor: pointer; position: relative; }}
  .tick:checked {{ background: var(--ok); border-color: var(--ok); }}
  .tick:checked::after {{ content: ''; position: absolute; left: 6px; top: 2px;
           width: 5px; height: 10px; border: solid var(--on-gold);
           border-width: 0 2px 2px 0; transform: rotate(45deg); }}
  .tick:focus-visible {{ outline: 2px solid var(--gold); outline-offset: 2px; }}
  .body {{ flex: 1; min-width: 0; }}
  .title {{ margin: 0 0 4px; font-size: 16px; }}
  .pri {{ display: inline-block; font-family: ui-monospace, monospace; font-size: 11px;
          padding: 1px 6px; border-radius: 99px; margin-right: 6px; vertical-align: 2px;
          border: 1px solid var(--border); color: var(--muted); }}
  .pri.p0 {{ color: var(--on-gold); background: var(--gold); border-color: transparent; }}
  .pri.p1 {{ color: var(--gold-light); border-color: var(--gold); }}
  .where {{ color: var(--faint); font-size: 12px; margin-left: 6px; }}
  .route {{ margin: 0; color: var(--text); font-size: 14px; }}
  .route span {{ color: var(--gold); }}
  details {{ margin-top: 4px; }}
  summary {{ cursor: pointer; color: var(--muted); font-size: 13px; }}
  .why, .comp {{ color: var(--muted); font-size: 13px; margin: 6px 0 0;
                 padding-left: 12px; border-left: 2px solid var(--line); }}
  .comp span {{ color: var(--faint); }}
  .stamp {{ margin: 4px 0 0; color: var(--faint); font-size: 12px; }}

  /* A row with a bug note on it. Marked rather than hidden: the point of the
     note is that this row is not finished, so it has to stay visible while
     the rest of the screen gets ticked off around it. */
  .row.flagged {{ opacity: 1; }}
  .row.flagged .body {{ border-left: 2px solid var(--bad); margin-left: -10px;
                        padding-left: 10px; }}
  .row.flagged .title::after {{ content: 'bug noted'; margin-left: 8px; font-size: 11px;
                                font-family: ui-monospace, monospace; color: var(--bad);
                                border: 1px solid var(--bad); border-radius: 99px;
                                padding: 1px 6px; vertical-align: 2px; }}
  .note {{ margin-top: 6px; }}
  .note-toggle {{ font-size: 13px; padding: 3px 8px; color: var(--muted); }}
  .note-field {{ display: block; width: 100%; margin-top: 6px; resize: vertical;
                 font: inherit; font-size: 14px; color: var(--text);
                 background: var(--panel); border: 1px solid var(--bad);
                 border-radius: 8px; padding: 8px 10px; }}
  .note-field:focus {{ outline: 2px solid var(--gold); outline-offset: 1px; }}

  /* The report sheet: everything noted, as text ready to hand back. */
  #sheet {{ position: fixed; inset: 0; background: rgb(0 0 0 / 0.6); z-index: 20;
            display: flex; align-items: center; justify-content: center; padding: 24px; }}
  #sheet > div {{ background: var(--ground); border: 1px solid var(--border);
                  border-radius: 12px; padding: 18px; width: min(900px, 100%);
                  max-height: 90vh; display: flex; flex-direction: column; gap: 10px; }}
  #sheet h2 {{ margin: 0; font-size: 19px; }}
  #sheet p {{ margin: 0; color: var(--muted); font-size: 13px; }}
  #report {{ flex: 1; min-height: 320px; width: 100%; resize: none; font-size: 13px;
             font-family: ui-monospace, monospace; color: var(--text);
             background: var(--panel); border: 1px solid var(--border);
             border-radius: 8px; padding: 10px; }}
  #sheet .tools {{ justify-content: flex-end; }}
  .closed {{ margin-top: 40px; padding-top: 12px; border-top: 1px solid var(--border);
             color: var(--muted); font-size: 13px; }}
  .hidden {{ display: none !important; }}
</style>
</head>
<body>
<header>
  <h1>Verification checklist</h1>
  <p class="sub">{total} shipped rows with a written test route, grouped by the screen you open.
     Ticks save in this browser as you click. Generated from {generated}.</p>
  <div class="bar"><i id="progress"></i></div>
  <div class="tools">
    <button data-pri="all" class="on">All {total}</button>
    <button data-pri="P0">P0 {p0}</button>
    <button data-pri="P1">P1 {p1}</button>
    <button data-pri="P2">P2 {p2}</button>
    <button id="hideDone">Hide done</button>
    <button id="onlyBugs">Only bug notes</button>
    <input type="search" id="find" placeholder="Search feature or route">
    <span class="spacer"></span>
    <span class="count" id="tally"></span>
    <button id="report-open">Bug report</button>
    <button id="save">Export progress</button>
    <button id="load">Import progress</button>
    <button id="reset">Reset</button>
  </div>
</header>
<main>{groups}
  <section class="closed">
    <h2>Not on this list</h2>
    <p>Rows closed by your own decision rather than by code, kept here so the totals reconcile.
       Nothing to test; nothing to reopen without deciding to.</p>
    <ul>{decisions}</ul>
  </section>
</main>

<div id="sheet" class="hidden">
  <div>
    <h2>Bug notes</h2>
    <p>Every row you left a note on, with the route that was meant to work.
       Copy this and hand it back for fixing.</p>
    <textarea id="report" readonly spellcheck="false"></textarea>
    <div class="tools">
      <button id="report-copy">Copy</button>
      <button id="report-download">Download as Markdown</button>
      <button id="report-close">Close</button>
    </div>
  </div>
</div>

<script id="rowmeta" type="application/json">{rowmeta}</script>
<script>
  var KEY = 'novalist-verification-v2';
  var OLD_KEY = 'novalist-verification-v1';
  var META = JSON.parse(document.getElementById('rowmeta').textContent);

  // {{ ticks: {{id: when}}, notes: {{id: text}} }}. v1 stored ticks alone at the
  // top level, so it is read once and carried forward rather than dropped.
  var state = {{ ticks: {{}}, notes: {{}} }};
  try {{
    var raw = JSON.parse(localStorage.getItem(KEY) || 'null');
    if (raw && raw.ticks) state = {{ ticks: raw.ticks || {{}}, notes: raw.notes || {{}} }};
    else {{
      var v1 = JSON.parse(localStorage.getItem(OLD_KEY) || 'null');
      if (v1) state.ticks = v1;
    }}
  }} catch (e) {{ /* a corrupt store starts empty rather than breaking the page */ }}

  var boxes = Array.prototype.slice.call(document.querySelectorAll('input.tick'));
  var notes = Array.prototype.slice.call(document.querySelectorAll('.note-field'));

  function stamp(id) {{
    var el = document.querySelector('[data-stamp="' + id + '"]');
    if (el) el.textContent = state.ticks[id]
      ? 'checked ' + new Date(state.ticks[id]).toLocaleString() : '';
  }}

  function noted(id) {{ return !!(state.notes[id] && state.notes[id].trim()); }}

  function paint() {{
    var done = 0, bugs = 0;
    boxes.forEach(function (box) {{
      var id = box.dataset.id;
      var on = !!state.ticks[id];
      box.checked = on;
      var row = box.closest('.row');
      row.classList.toggle('done', on);
      row.classList.toggle('flagged', noted(id));
      stamp(id);
      if (on) done++;
      if (noted(id)) bugs++;
    }});
    notes.forEach(function (field) {{
      var id = field.dataset.note;
      if (field.value !== (state.notes[id] || '')) field.value = state.notes[id] || '';
      field.classList.toggle('hidden', !noted(id) && field !== document.activeElement);
      var button = document.querySelector('[data-note-for="' + id + '"]');
      if (button) button.textContent = noted(id) ? 'Edit bug note' : 'Add a bug note';
    }});
    document.getElementById('progress').style.width =
      (boxes.length ? (100 * done / boxes.length) : 0) + '%';
    document.getElementById('tally').textContent =
      done + ' / ' + boxes.length + ' checked' + (bugs ? '  |  ' + bugs + ' with bug notes' : '');
    document.getElementById('onlyBugs').textContent =
      bugs ? 'Only bug notes (' + bugs + ')' : 'Only bug notes';
    document.querySelectorAll('.group').forEach(function (group) {{
      group.querySelector('.gdone').textContent =
        group.querySelectorAll('input.tick:checked').length;
    }});
  }}

  function persist() {{ localStorage.setItem(KEY, JSON.stringify(state)); }}

  boxes.forEach(function (box) {{
    box.addEventListener('change', function () {{
      if (box.checked) state.ticks[box.dataset.id] = Date.now();
      else delete state.ticks[box.dataset.id];
      persist();
      paint();
      filter();
    }});
  }});

  // Saved as typed. A note lost because the page was closed before some Save
  // button was found is a bug report that never reaches anybody.
  notes.forEach(function (field) {{
    field.addEventListener('input', function () {{
      var id = field.dataset.note;
      if (field.value.trim()) state.notes[id] = field.value;
      else delete state.notes[id];
      persist();
      var row = field.closest('.row');
      row.classList.toggle('flagged', noted(id));
      var button = document.querySelector('[data-note-for="' + id + '"]');
      if (button) button.textContent = noted(id) ? 'Edit bug note' : 'Add a bug note';
      tallyOnly();
    }});
    field.addEventListener('blur', function () {{ paint(); filter(); }});
  }});

  function tallyOnly() {{
    var done = boxes.filter(function (b) {{ return b.checked; }}).length;
    var bugs = Object.keys(state.notes).length;
    document.getElementById('tally').textContent =
      done + ' / ' + boxes.length + ' checked' + (bugs ? '  |  ' + bugs + ' with bug notes' : '');
    document.getElementById('onlyBugs').textContent =
      bugs ? 'Only bug notes (' + bugs + ')' : 'Only bug notes';
  }}

  document.querySelectorAll('.note-toggle').forEach(function (button) {{
    button.addEventListener('click', function () {{
      var field = document.querySelector('[data-note="' + button.dataset.noteFor + '"]');
      field.classList.remove('hidden');
      field.focus();
    }});
  }});

  var pri = 'all', hideDone = false, onlyBugs = false, term = '';

  function filter() {{
    document.querySelectorAll('.row').forEach(function (row) {{
      var byPri = pri === 'all' || row.dataset.pri === pri;
      var byDone = !hideDone || !row.classList.contains('done');
      var byBug = !onlyBugs || row.classList.contains('flagged');
      var byTerm = !term || row.textContent.toLowerCase().indexOf(term) !== -1;
      row.classList.toggle('hidden', !(byPri && byDone && byBug && byTerm));
    }});
    document.querySelectorAll('.group').forEach(function (group) {{
      var any = group.querySelector('.row:not(.hidden)');
      group.classList.toggle('hidden', !any);
    }});
  }}

  document.querySelectorAll('[data-pri]').forEach(function (button) {{
    button.addEventListener('click', function () {{
      pri = button.dataset.pri;
      document.querySelectorAll('[data-pri]').forEach(function (b) {{
        b.classList.toggle('on', b === button);
      }});
      filter();
    }});
  }});

  document.getElementById('hideDone').addEventListener('click', function () {{
    hideDone = !hideDone;
    this.classList.toggle('on', hideDone);
    filter();
  }});

  document.getElementById('find').addEventListener('input', function () {{
    term = this.value.trim().toLowerCase();
    filter();
  }});

  document.getElementById('onlyBugs').addEventListener('click', function () {{
    onlyBugs = !onlyBugs;
    this.classList.toggle('on', onlyBugs);
    filter();
  }});

  document.getElementById('reset').addEventListener('click', function () {{
    if (!confirm('Clear every tick and every bug note on this checklist?')) return;
    state = {{ ticks: {{}}, notes: {{}} }};
    persist();
    paint();
    filter();
  }});

  // --- the bug report -------------------------------------------------

  function buildReport() {{
    var ids = Object.keys(state.notes).filter(function (id) {{
      return state.notes[id] && state.notes[id].trim();
    }});
    if (!ids.length) return 'No bug notes yet.';

    ids.sort(function (a, b) {{
      var A = META[a] || {{}}, B = META[b] || {{}};
      return (A.pri || '').localeCompare(B.pri || '') ||
             (A.group || '').localeCompare(B.group || '');
    }});

    var out = ['# Verification bug notes', '',
               ids.length + ' of ' + boxes.length + ' rows have a note. ' +
               'From docs/plans/verification-checklist.html.', ''];
    var group = null;
    ids.forEach(function (id) {{
      var m = META[id] || {{ name: id, pri: '?', group: 'Elsewhere', route: '' }};
      if (m.group !== group) {{ group = m.group; out.push('## ' + group, ''); }}
      out.push('### ' + m.name + '  (' + m.pri + ')');
      out.push('');
      if (m.route) out.push('- Route that was meant to work: ' + m.route);
      out.push('- Verified working: ' + (state.ticks[id] ? 'yes, ticked anyway' : 'no'));
      out.push('');
      out.push(state.notes[id].trim());
      out.push('');
    }});
    // fromCharCode rather than a backslash escape: this template is rendered
    // by Python first, so an escape here has to survive that pass as well.
    return out.join(String.fromCharCode(10));
  }}

  var sheet = document.getElementById('sheet');
  var report = document.getElementById('report');

  function openSheet() {{
    report.value = buildReport();
    sheet.classList.remove('hidden');
    report.focus();
    report.select();
  }}

  document.getElementById('report-open').addEventListener('click', openSheet);
  document.getElementById('report-close').addEventListener('click', function () {{
    sheet.classList.add('hidden');
  }});
  sheet.addEventListener('click', function (event) {{
    if (event.target === sheet) sheet.classList.add('hidden');
  }});
  document.addEventListener('keydown', function (event) {{
    if (event.key === 'Escape') sheet.classList.add('hidden');
  }});

  // The clipboard API is blocked in some browsers on a file:// page, so the
  // text is always on screen and selected - copy works whether or not the
  // button does.
  document.getElementById('report-copy').addEventListener('click', function () {{
    var button = this;
    var restore = function (word) {{
      button.textContent = word;
      setTimeout(function () {{ button.textContent = 'Copy'; }}, 1500);
    }};
    report.select();
    if (navigator.clipboard && navigator.clipboard.writeText) {{
      navigator.clipboard.writeText(report.value)
        .then(function () {{ restore('Copied'); }})
        .catch(function () {{ restore('Press Ctrl+C'); }});
    }} else {{
      restore('Press Ctrl+C');
    }}
  }});

  document.getElementById('report-download').addEventListener('click', function () {{
    var blob = new Blob([report.value], {{ type: 'text/markdown' }});
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'novalist-bug-notes.md';
    a.click();
    URL.revokeObjectURL(a.href);
  }});

  // Progress lives in this browser, so it can be taken out and put back -
  // onto another machine, or into the repo beside the audit.
  document.getElementById('save').addEventListener('click', function () {{
    var blob = new Blob([JSON.stringify(state, null, 1)], {{ type: 'application/json' }});
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'novalist-verification-progress.json';
    a.click();
    URL.revokeObjectURL(a.href);
  }});

  document.getElementById('load').addEventListener('click', function () {{
    var input = document.createElement('input');
    input.type = 'file';
    input.accept = 'application/json';
    input.addEventListener('change', function () {{
      var file = input.files && input.files[0];
      if (!file) return;
      file.text().then(function (text) {{
        try {{
          state = JSON.parse(text);
          persist();
          paint();
          filter();
        }} catch (e) {{ alert('That file is not checklist progress.'); }}
      }});
    }});
    input.click();
  }});

  paint();
  filter();
</script>
</body>
</html>
"""


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default=str(OUT))
    args = parser.parse_args()

    rows, decisions = parse()
    page = render(rows, decisions)
    out = pathlib.Path(args.out)
    io.open(out, "w", encoding="utf-8", newline="\n").write(page)

    todo = [r for r in rows if r["done"] and r["route"]]
    missing = [r for r in rows if r["done"] and not r["route"]]
    print(f"{len(rows)} findings, {sum(1 for r in rows if r['done'])} shipped, "
          f"{len(todo)} with a test route")
    if missing:
        print(f"\n{len(missing)} shipped rows carry no TEST route and are not on the list:")
        for row in missing:
            print(f"  [{row['pri']}] {row['name']}")
    print(f"\nwrote {out.as_posix()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Placement doctor: one command, one persistent home.

Novalist's interface grew a command at a time, and each one picked a home on
the day it was written. Comment ended up in three of them; the four alignments
in two, one of which only appeared when text was selected; the panel toggles in
both the toolbar and the View menu. Nothing connected the kind of a command to
the kind of container it belonged in, so there was nothing for a writer to
learn, and no build step could tell that anything was wrong.

The law, from docs/plans/ui-restructure.md:

    A command's scope decides its home, it has exactly one persistent home,
    and it is also in the command palette.

This checks the first two clauses. The third holds by construction: the palette
is built from the same registry this reads.

How it works
------------
`app/src/renderer/src/shell/commands.ts` is the registry. Every command
declares a scope, which maps to a container through DEFAULT_HOME; a command
that needs a different home declares one, and the type system already forces a
`homeNote` beside it.

Every surface that renders commands marks itself:

    placement-container: <name>
    ...
    placement-container: end

and every control inside carries the id it runs, as `data-command="id"` in
markup or `command: 'id'` in a descriptor object. The doctor reads those
regions and compares what is rendered against what was declared.

Failures
--------
  * a command rendered in a container that is not its declared home
  * a command rendered in two containers
  * a command in the registry that no container renders
  * a `data-command` naming an id the registry does not have
  * a `data-command` outside any declared container region
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "app"
REGISTRY = APP / "src/renderer/src/shell/commands.ts"

# Files that are allowed to render commands. Everything else is scanned too, so
# a stray data-command somewhere new is reported rather than silently ignored.
SCAN_GLOBS = ("src/renderer/src/**/*.ts", "src/renderer/src/**/*.tsx")
SCAN_FILES = ("src/renderer/public/editor/editor.html",)

# `placement-container: <name>` opens a region, `: end` closes one. A region
# marked `list` holds nothing but command ids as bare string literals - which is
# what the menu bar's order arrays are - so every id-shaped literal in it counts
# as rendered. Anywhere else only an explicit `data-command` or `command:` does,
# because ordinary source is full of dotted strings that are locale keys.
REGION_START = re.compile(r"placement-container:\s*([A-Za-z]+)(\s+list)?")
# `data-command="a b"` in markup, and `command: 'a'` in a descriptor object.
RENDERED = re.compile(r"""data-command=["']([^"'{}]+)["']|(?<![\w-])command:\s*'([^']+)'""")
LITERAL = re.compile(r"'([^']+)'")

COMMAND_ID = re.compile(r"^[a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+$")


@dataclass
class Command:
    id: str
    scope: str
    home: str
    declared_home: bool = False
    containers: set[str] = field(default_factory=set)


def fail(problems: list[str]) -> int:
    for problem in problems:
        print(f"  {problem}")
    print(f"\nplacement-doctor: {len(problems)} problem(s).")
    return 1


def parse_default_home(source: str) -> dict[str, str]:
    """The scope -> container table, read from the registry rather than restated."""
    block = re.search(
        r"export const DEFAULT_HOME: Record<CommandScope, CommandContainer> = \{(.*?)\n\}",
        source,
        re.S,
    )
    if not block:
        raise SystemExit("placement-doctor: DEFAULT_HOME is not in commands.ts")
    return dict(re.findall(r"(\w+):\s*'(\w+)'", block.group(1)))


def parse_registry(source: str, default_home: dict[str, str]) -> list[Command]:
    """Every command the registry declares, generated families included.

    Two families are built from a list rather than written out: the navigation
    commands, one per view, and the paragraph styles. Their lists are read here
    by name, which is the price of not having 27 near-identical literals in the
    registry - and the reason both are marked in commands.ts.
    """
    commands: list[Command] = []

    # Static entries: an `id:` line opens a record, and scope/home follow before
    # the next one.
    current: dict[str, str] | None = None
    for line in source.splitlines():
        opened = re.match(r"\s*id: '([^']+)',", line)
        if opened:
            if current:
                commands.append(build(current, default_home))
            current = {"id": opened.group(1)}
            continue
        if current is None:
            continue
        scope = re.match(r"\s*scope: '(\w+)'", line)
        if scope:
            current["scope"] = scope.group(1)
        home = re.match(r"\s*home: '(\w+)'", line)
        if home:
            current["home"] = home.group(1)
    if current:
        commands.append(build(current, default_home))

    for view in string_list(source, "NAV_VIEWS"):
        commands.append(Command(f"nav.{view}", "application", default_home["application"]))
    for style in string_list(source, "PARAGRAPH_STYLES"):
        name = style or "body"
        commands.append(
            Command(f"paragraph.style.{name}", "paragraph", default_home["paragraph"])
        )
    return commands


def build(record: dict[str, str], default_home: dict[str, str]) -> Command:
    scope = record.get("scope", "")
    if scope not in default_home:
        raise SystemExit(f"placement-doctor: {record['id']} has no readable scope")
    declared = "home" in record
    return Command(record["id"], scope, record.get("home", default_home[scope]), declared)


def string_list(source: str, name: str) -> list[str]:
    """The string literals of a named const array."""
    match = re.search(rf"const {name}[^=]*= \[(.*?)\]", source, re.S)
    if not match:
        raise SystemExit(f"placement-doctor: {name} is not in commands.ts")
    return re.findall(r"'([^']*)'", match.group(1))


def scan(path: Path) -> tuple[dict[str, set[str]], list[str]]:
    """Command ids rendered per container, plus anything rendered outside one."""
    rendered: dict[str, set[str]] = {}
    loose: list[str] = []
    container: str | None = None
    as_list = False
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        marker = REGION_START.search(line)
        if marker:
            ending = marker.group(1) == "end"
            container = None if ending else marker.group(1)
            as_list = not ending and marker.group(2) is not None
            continue

        if as_list and container is not None:
            for command in LITERAL.findall(line):
                if COMMAND_ID.match(command):
                    rendered.setdefault(command, set()).add(container)
            continue

        for attribute, descriptor in RENDERED.findall(line):
            for command in (attribute or descriptor).split():
                if not COMMAND_ID.match(command):
                    continue
                if container is None:
                    loose.append(f"{path.relative_to(ROOT)}:{number} renders {command} "
                                 "outside any placement-container region")
                else:
                    rendered.setdefault(command, set()).add(container)
    return rendered, loose


def main() -> int:
    source = REGISTRY.read_text(encoding="utf-8")
    default_home = parse_default_home(source)
    commands = parse_registry(source, default_home)

    problems: list[str] = []

    by_id: dict[str, Command] = {}
    for command in commands:
        if command.id in by_id:
            problems.append(f"{command.id} is declared twice in the registry")
        by_id[command.id] = command

    files = [p for pattern in SCAN_GLOBS for p in APP.glob(pattern)]
    files += [APP / name for name in SCAN_FILES]

    for path in sorted(set(files)):
        rendered, loose = scan(path)
        problems.extend(loose)
        for command_id, containers in rendered.items():
            command = by_id.get(command_id)
            if command is None:
                problems.append(
                    f"{path.relative_to(ROOT)} renders {command_id}, "
                    "which is not in the command registry"
                )
                continue
            command.containers |= containers

    for command in commands:
        if not command.containers:
            problems.append(
                f"{command.id} ({command.scope}) has no persistent home: "
                f"nothing renders it, and its scope puts it in {command.home}"
            )
        elif len(command.containers) > 1:
            where = ", ".join(sorted(command.containers))
            problems.append(f"{command.id} has more than one persistent home: {where}")
        elif next(iter(command.containers)) != command.home:
            found = next(iter(command.containers))
            problems.append(
                f"{command.id} ({command.scope}) is rendered in {found} "
                f"but its declared home is {command.home}"
            )

    if problems:
        print("placement-doctor: the placement law is broken.\n")
        return fail(problems)

    overrides = [c for c in commands if c.declared_home]
    print(f"placement-doctor: {len(commands)} commands, one home each.")
    for container in sorted({c.home for c in commands}):
        count = sum(1 for c in commands if c.home == container)
        print(f"  {container}: {count}")
    if overrides:
        print(f"  {len(overrides)} declare a home other than their scope's default.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

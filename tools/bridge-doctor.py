#!/usr/bin/env python3
"""Checks the editor bridge is wired at both ends.

The renderer talks to the editor iframe through the EditorWindow interface in
`editorBridge.ts`. That interface is a declaration and nothing verified it:
a method could be declared, implemented in editor.html, and called by nobody -
which is exactly how "delete this footnote" shipped removing the row from the
list and leaving the marker sitting in the prose. `removeFootnoteById` and
`removeCommentById` were both fully built and wired to no button.

This is the same failure the project rules describe for the Settings override
switch and that `rpc-doctor.py` catches for the JSON-RPC boundary. The editor
bridge is the other boundary in this app, and it had no doctor.

Two directions, both fatal:

  declared but never called   - dead surface, or a feature wired to nothing
  called but never declared   - a typo that fails silently at runtime

A third is reported but not fatal: declared and called but missing from
editor.html, which the TypeScript compiler cannot see because the iframe's
globals are not typed.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BRIDGE = ROOT / "app/src/renderer/src/views/editor/editorBridge.ts"
EDITOR_HTML = ROOT / "app/src/renderer/public/editor/editor.html"
RENDERER = ROOT / "app/src/renderer/src"

# Methods the host never calls on purpose: they exist for an extension or a
# future caller, or they are lifecycle hooks the frame invokes on itself.
ALLOWED_UNCALLED: set[str] = set()

# A member declaration inside the interface: `name(args): type` — not a comment
# and not a property.
MEMBER = re.compile(r"^\s{2}(\w+)\s*\(", re.MULTILINE)


def declared_methods(text: str) -> list[str]:
    """Every method on the EditorWindow interface, in declaration order."""
    start = text.index("export interface EditorWindow")
    depth = 0
    end = start
    for i in range(text.index("{", start), len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                end = i
                break
    return MEMBER.findall(text[start:end])


def main() -> int:
    bridge = BRIDGE.read_text(encoding="utf-8")
    declared = declared_methods(bridge)

    # Every renderer source except the bridge itself, where the names only
    # appear as declarations.
    callers: dict[str, list[str]] = {}
    used: set[str] = set()
    for path in RENDERER.rglob("*.ts*"):
        if path == BRIDGE:
            continue
        text = path.read_text(encoding="utf-8", errors="ignore")
        for name in declared:
            if re.search(r"\.%s\s*\(" % re.escape(name), text):
                used.add(name)
                callers.setdefault(name, []).append(
                    str(path.relative_to(ROOT)).replace("\\", "/"))

    html = EDITOR_HTML.read_text(encoding="utf-8", errors="ignore")
    # `window.name = ...`, or a function declared at the top level of the
    # script - a classic script's top-level declarations become globals, so
    # both forms are callable from the host. A function nested inside another
    # is not, which is why the column matters.
    implemented = {
        name for name in declared
        if re.search(r"window\.%s\s*=" % re.escape(name), html)
        or re.search(r"^function\s+%s\s*\(" % re.escape(name), html, re.MULTILINE)
    }

    uncalled = [n for n in declared if n not in used and n not in ALLOWED_UNCALLED]
    unimplemented = [n for n in declared if n not in implemented]

    print(f"editor bridge: {len(declared)} methods declared, {len(used)} called")

    failed = False
    if uncalled:
        failed = True
        print("\nDeclared on the bridge and called by nothing:")
        for name in uncalled:
            built = "implemented in editor.html" if name in implemented else "not implemented either"
            print(f"  {name:<28} ({built})")
        print("\n  A bridge method nothing calls is a feature wired to nothing.")
        print("  Either call it, or delete it, or add it to ALLOWED_UNCALLED with a reason.")

    if unimplemented:
        print("\nDeclared and called, but no matching global in editor.html:")
        for name in unimplemented:
            for where in callers.get(name, ["(no caller)"]):
                print(f"  {name:<28} called from {where}")
        print("\n  These fail silently at runtime: the iframe's globals are untyped,")
        print("  so TypeScript cannot see the call going nowhere.")
        failed = True

    if not failed:
        print("every bridge method is declared, implemented, and called")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

"""Locale source roots and reference patterns for each application."""

import re
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent


@dataclass
class Target:
    name: str
    locales_dir: Path
    scan_roots: list[Path]
    scan_exts: set[str]
    literal_patterns: list[re.Pattern]
    dynamic_prefixes: set[str]
    # When True, also treat any bare quoted string that matches an en.json key as a
    # reference (covers keys passed to t() via a variable, e.g. t(group.key)).
    match_bare_literals: bool = False
    # Extension webviews often receive an already-translated string map from
    # C#. Keys on the left of that map are transport slots, not locale keys.
    exclude_supplied_slots: bool = False
    # Auto-discovered template-literal prefixes (react) added at scan time.
    extra_dynamic: set[str] = field(default_factory=set)


DESKTOP = Target(
    name="desktop",
    locales_dir=REPO_ROOT / "Novalist.Desktop" / "Assets" / "Locales",
    scan_roots=[REPO_ROOT / "Novalist.Desktop", REPO_ROOT / "Novalist.Core"],
    scan_exts={".cs", ".axaml"},
    literal_patterns=[
        re.compile(r'Loc\.T\("([^"]+)"'),
        re.compile(r'Loc\.Instance\["([^"]+)"\]'),
        re.compile(r"\{loc:Loc\s+([\w.]+)\}"),
        re.compile(r"\{loc:Loc\s+Key=([\w.]+)\}"),
        re.compile(r'\[Loc\]\("([^"]+)"'),
    ],
    dynamic_prefixes={
        "emotion.",
        "entityEditor.locationTypePlain",
        "entityEditor.description",
        "entityEditor.origin",
        "entityEditor.category",
        "extensions.",
        "settings.",
        "hotkeys.",
        "wizard.entity.",
        "wizard.project.",
        "wizard.interview.",
        "wizard.ai.",
        "relationships.parent",
        "relationships.child",
        "relationships.partner",
        "relationships.sibling",
        "relationships.pseudo",
    },
)

REACT = Target(
    name="react",
    locales_dir=REPO_ROOT / "app" / "src" / "renderer" / "src" / "locales",
    scan_roots=[REPO_ROOT / "app" / "src" / "renderer" / "src"],
    scan_exts={".ts", ".tsx"},
    literal_patterns=[
        re.compile(r"\bt\(\s*'([^']+)'\s*[,)]"),
        re.compile(r'\bt\(\s*"([^"]+)"\s*[,)]'),
        re.compile(r"\bi18n(?:ext)?\.t\(\s*'([^']+)'\s*[,)]"),
        re.compile(r'\bi18n(?:ext)?\.t\(\s*"([^"]+)"\s*[,)]'),
    ],
    # Keys reached via variables/dynamic composition; kept from dead/missing noise.
    dynamic_prefixes={
        "shell.view.",
        "shell.group",
        "focusPeek.type",
        "statusBar.readabilityLevel.",
        "hotkeys.category.",
    },
    match_bare_literals=True,
)

# ── Extensions ──────────────────────────────────────────────────────
#
# Extensions ship their own Locales folder and their own C# and web code, and
# nothing checked them. A missing key there is exactly as broken as one here -
# the writer sees a raw key in a panel - and it was invisible because the
# doctor only ever looked inside this repo.
#
# They are sibling checkouts rather than submodules, so they are discovered
# when present and skipped in silence when they are not: a clone with only this
# repo still has to pass.
EXTENSION_WORKSPACES = [
    REPO_ROOT.parent / "novalist-extension",
    REPO_ROOT.parent / "novalist-aiassistant",
]


def extension_targets() -> list[Target]:
    """One target per extension found beside this repo, deepest first."""
    found: list[Target] = []
    for workspace in EXTENSION_WORKSPACES:
        if not workspace.is_dir():
            continue
        for locales in sorted(workspace.rglob("Locales")):
            if not locales.is_dir() or not (locales / "en.json").exists():
                continue
            if any(part in {"bin", "obj", "node_modules"} for part in locales.parts):
                continue
            # The extension's own project folder: its C# and its web pages both
            # ask for keys, so both are scanned.
            root = locales.parent
            found.append(
                Target(
                    name=f"ext:{root.name}",
                    locales_dir=locales,
                    scan_roots=[root],
                    scan_exts={".cs", ".axaml", ".ts", ".tsx", ".js", ".html"},
                    literal_patterns=[
                        re.compile(r'\b_?[Ll]oc\.T\(\s*"([^"]+)"\s*[,)]'),
                        re.compile(r'\bT\(\s*"([^"]+)"\s*[,)]'),
                        re.compile(r"\bt\(\s*'([^']+)'\s*[,)]"),
                        re.compile(r'\bt\(\s*"([^"]+)"\s*[,)]'),
                        re.compile(r'data-i18n="([^"]+)"'),
                    ],
                    dynamic_prefixes=set(),
                    exclude_supplied_slots=True,
                )
            )
    return found

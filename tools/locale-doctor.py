#!/usr/bin/env python3
"""
locale-doctor — finds dead, missing, and placeholder-drift keys in Novalist
locale JSON files. Aware of dynamic key concatenation patterns.

Run from repo root:
    python tools/locale-doctor.py
    python tools/locale-doctor.py --prune        # delete dead keys (dry-run first)
    python tools/locale-doctor.py --prune --apply

Exit code 0 if clean, 1 if drift detected (suitable for CI).
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Iterable

REPO_ROOT = Path(__file__).resolve().parent.parent
DESKTOP = REPO_ROOT / "Novalist.Desktop"
LOCALES_DIR = DESKTOP / "Assets" / "Locales"

# Keys built dynamically at runtime — never prune anything under these prefixes.
# Add new prefixes here when new dynamic patterns are introduced.
DYNAMIC_PREFIXES = {
    "emotion.",
    "entityEditor.locationTypePlain",
    "entityEditor.description",
    "entityEditor.origin",
    "entityEditor.category",
    "extensions.",            # extension authors register categories at runtime
    "settings.",              # broad — many settings keys reached via reflection-like patterns
    "hotkeys.",               # bound through HotkeyDescriptor lists per category
    "wizard.entity.",         # wizard system loads keys by entity-type
    "wizard.project.",
    "wizard.interview.",
    "wizard.ai.",
    "relationships.parent",
    "relationships.child",
    "relationships.partner",
    "relationships.sibling",
    "relationships.pseudo",
}

# Source roots scanned for static literal references.
SCAN_ROOTS = [DESKTOP, REPO_ROOT / "Novalist.Core"]
SCAN_EXTS = {".cs", ".axaml"}

LITERAL_PATTERNS = [
    re.compile(r'Loc\.T\("([^"]+)"'),
    re.compile(r'Loc\.Instance\["([^"]+)"\]'),
    re.compile(r'\{loc:Loc\s+([\w.]+)\}'),
    re.compile(r'\{loc:Loc\s+Key=([\w.]+)\}'),
    re.compile(r'\[Loc\]\("([^"]+)"'),
]

PLACEHOLDER_RE = re.compile(r'\{(\d+)\}')


def flatten(prefix: str, value, out: dict[str, str]) -> None:
    """Flatten nested dict to dotted keys -> string values."""
    if isinstance(value, dict):
        for k, v in value.items():
            flatten(f"{prefix}.{k}" if prefix else k, v, out)
    elif isinstance(value, list):
        # Lists used for synonyms (relationships.parent etc). Treat as leaf.
        out[prefix] = "[]"
    else:
        out[prefix] = str(value)


def load_locale(path: Path) -> dict[str, str]:
    with path.open(encoding="utf-8-sig") as f:
        data = json.load(f)
    flat: dict[str, str] = {}
    flatten("", data, flat)
    return flat


def scan_static_references() -> set[str]:
    refs: set[str] = set()
    for root in SCAN_ROOTS:
        if not root.exists():
            continue
        for path in root.rglob("*"):
            if path.suffix not in SCAN_EXTS:
                continue
            if "bin" in path.parts or "obj" in path.parts:
                continue
            try:
                text = path.read_text(encoding="utf-8-sig")
            except Exception:
                continue
            for pat in LITERAL_PATTERNS:
                for m in pat.finditer(text):
                    refs.add(m.group(1))
    return refs


def is_dynamic(key: str) -> bool:
    return any(key.startswith(p) for p in DYNAMIC_PREFIXES)


def placeholder_set(s: str) -> set[str]:
    return set(PLACEHOLDER_RE.findall(s))


def remove_key(data: dict, dotted: str) -> bool:
    parts = dotted.split(".")
    if not parts:
        return False
    parent = data
    for p in parts[:-1]:
        if not isinstance(parent, dict) or p not in parent:
            return False
        parent = parent[p]
    if isinstance(parent, dict) and parts[-1] in parent:
        del parent[parts[-1]]
        return True
    return False


def prune_empty(data):
    """Recursively drop empty dicts left behind by key removal."""
    if not isinstance(data, dict):
        return data
    for k in list(data.keys()):
        child = prune_empty(data[k])
        if isinstance(child, dict) and not child:
            del data[k]
    return data


def main() -> int:
    parser = argparse.ArgumentParser(description="Locale doctor for Novalist.")
    parser.add_argument("--prune", action="store_true", help="Remove dead keys.")
    parser.add_argument("--apply", action="store_true", help="Actually write files (with --prune).")
    parser.add_argument("--no-fail-on-dead", action="store_true",
                        help="Report dead keys but exit 0.")
    args = parser.parse_args()

    en_path = LOCALES_DIR / "en.json"
    if not en_path.exists():
        print(f"ERROR: {en_path} not found", file=sys.stderr)
        return 2

    en = load_locale(en_path)
    other_locales = {
        p.stem: load_locale(p)
        for p in LOCALES_DIR.glob("*.json")
        if p != en_path
    }

    refs = scan_static_references()

    dead: list[str] = []
    dynamic_kept: list[str] = []
    for key in sorted(en.keys()):
        if en[key] == "[]":
            continue  # list values (relationships.parent synonyms) — keep
        if key in refs:
            continue
        if is_dynamic(key):
            dynamic_kept.append(key)
            continue
        dead.append(key)

    missing = sorted(refs - set(en.keys()))

    placeholder_drift: list[str] = []
    for lang, locale in other_locales.items():
        for key, en_value in en.items():
            if key not in locale:
                continue
            en_ph = placeholder_set(en_value)
            other_ph = placeholder_set(locale[key])
            if en_ph != other_ph:
                placeholder_drift.append(f"{lang}::{key}  en={sorted(en_ph)} {lang}={sorted(other_ph)}")

    print("=== Locale doctor report ===")
    print(f"en.json keys:         {len(en)}")
    for lang, locale in other_locales.items():
        print(f"{lang}.json keys:       {len(locale)}")
    print(f"static references:    {len(refs)}")
    print(f"dynamic-prefix keys:  {len(dynamic_kept)} (kept)")
    print(f"dead keys:            {len(dead)}")
    print(f"missing keys:         {len(missing)}")
    print(f"placeholder drift:    {len(placeholder_drift)}")

    if missing:
        print("\n-- MISSING keys (referenced in code, absent from en.json) --")
        for k in missing:
            print(f"  {k}")

    if placeholder_drift:
        print("\n-- PLACEHOLDER DRIFT --")
        for line in placeholder_drift:
            print(f"  {line}")

    if dead:
        print("\n-- DEAD keys (no static reference, not under dynamic prefix) --")
        for k in dead[:50]:
            print(f"  {k}")
        if len(dead) > 50:
            print(f"  ... +{len(dead) - 50} more")

    if args.prune and dead:
        print(f"\n-- PRUNING {len(dead)} dead keys --")
        for path in LOCALES_DIR.glob("*.json"):
            with path.open(encoding="utf-8-sig") as f:
                data = json.load(f)
            removed = 0
            for k in dead:
                if remove_key(data, k):
                    removed += 1
            data = prune_empty(data)
            if args.apply:
                path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n",
                                encoding="utf-8")
                print(f"  {path.name}: removed {removed} keys, written")
            else:
                print(f"  {path.name}: would remove {removed} keys (dry-run; pass --apply to write)")

    has_drift = bool(missing) or bool(placeholder_drift) or (bool(dead) and not args.no_fail_on_dead)
    return 1 if has_drift else 0


if __name__ == "__main__":
    sys.exit(main())

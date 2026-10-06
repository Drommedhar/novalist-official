#!/usr/bin/env python3
"""
locale-doctor — finds dead, missing, untranslated, and placeholder-drift keys in
Novalist locale JSON files. Checks BOTH the frozen Avalonia app (Novalist.Desktop
+ Novalist.Core, Loc.T/{loc:Loc} references) and the new Electron/React app
(app/src/renderer, i18next t('...') references). Aware of dynamic key patterns.

Run from repo root:
    python tools/locale-doctor.py                 # both targets
    python tools/locale-doctor.py --target react   # react only
    python tools/locale-doctor.py --target desktop  # desktop only
    python tools/locale-doctor.py --prune          # delete dead keys (dry-run first)
    python tools/locale-doctor.py --prune --apply

Exit code 0 if clean, 1 if drift detected (suitable for CI).
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from locale_spelling import spelling_faults
from locale_targets import DESKTOP, REACT, Target, extension_targets

# ── {N} (desktop) and {{name}} (i18next) placeholders ──────────────────────────
PLACEHOLDER_NUM_RE = re.compile(r"\{(\d+)\}")
PLACEHOLDER_NAME_RE = re.compile(r"\{\{(\w+)\}\}")

# Extracts the static prefix of a template-literal key, e.g. t(`shell.view.${v}`)
# -> "shell.view." so dynamically-built keys are treated as a dynamic prefix.
REACT_TEMPLATE_PREFIX_RE = re.compile(r"\bt\(\s*`([\w.]*)\$\{")


def flatten(prefix: str, value, out: dict[str, str]) -> None:
    if isinstance(value, dict):
        for k, v in value.items():
            flatten(f"{prefix}.{k}" if prefix else k, v, out)
    elif isinstance(value, list):
        out[prefix] = "[]"
    else:
        out[prefix] = str(value)


def load_locale(path: Path) -> dict[str, str]:
    with path.open(encoding="utf-8-sig") as f:
        data = json.load(f)
    flat: dict[str, str] = {}
    flatten("", data, flat)
    return flat


def scan_references(target: Target, en_keys: set[str]) -> set[str]:
    refs: set[str] = set()
    bare = (
        re.compile(r"['\"]([\w][\w.]*\.[\w.]+)['\"]")
        if target.match_bare_literals
        else None
    )
    supplied_re = re.compile(r'\[\s*"([^"]+)"\s*\]\s*=\s*[^;\r\n]*\bT\(')
    sources: list[tuple[Path, str]] = []
    for root in target.scan_roots:
        if not root.exists():
            continue
        for path in root.rglob("*"):
            if path.suffix not in target.scan_exts:
                continue
            if any(
                p in ("bin", "obj", "node_modules", "dist", "out") for p in path.parts
            ):
                continue
            text = path.read_text(encoding="utf-8-sig")
            sources.append((path, text))

    supplied: set[str] = set()
    if target.exclude_supplied_slots:
        for _, text in sources:
            supplied.update(m.group(1) for m in supplied_re.finditer(text))

    for _, text in sources:
        for pat in target.literal_patterns:
            for m in pat.finditer(text):
                if m.group(1) not in supplied:
                    refs.add(m.group(1))
        for m in REACT_TEMPLATE_PREFIX_RE.finditer(text):
            if m.group(1):
                target.extra_dynamic.add(m.group(1))
        # Bare quoted strings that exactly match a known key (variable-passed keys).
        if bare is not None:
            for m in bare.finditer(text):
                if m.group(1) in en_keys:
                    refs.add(m.group(1))
    return refs


def placeholders(s: str) -> tuple[frozenset, frozenset]:
    return (
        frozenset(PLACEHOLDER_NUM_RE.findall(s)),
        frozenset(PLACEHOLDER_NAME_RE.findall(s)),
    )


def is_dynamic(target: Target, key: str) -> bool:
    return any(
        key.startswith(p) for p in target.dynamic_prefixes | target.extra_dynamic
    )


def remove_key(data: dict, dotted: str) -> bool:
    parts = dotted.split(".")
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
    if not isinstance(data, dict):
        return data
    for k in list(data.keys()):
        child = prune_empty(data[k])
        if isinstance(child, dict) and not child:
            del data[k]
    return data


def report_details(target_name: str, findings: dict[str, list[str]]) -> None:
    missing, untranslated, drift, mangled, dead = (
        findings[key] for key in ("missing", "untranslated", "drift", "mangled", "dead")
    )
    if missing:
        print(
            f"\n  -- MISSING ({target_name}: referenced in code, absent from en.json) --"
        )
        for k in missing:
            print(f"    {k}")
    if untranslated:
        print(
            f"\n  -- UNTRANSLATED ({target_name}: in en.json, absent from a locale) --"
        )
        for k in untranslated[:80]:
            print(f"    {k}")
        if len(untranslated) > 80:
            print(f"    ... +{len(untranslated) - 80} more")
    if drift:
        print(f"\n  -- PLACEHOLDER DRIFT ({target_name}) --")
        for line in drift:
            print(f"    {line}")
    if mangled:
        print(
            f"\n  -- SPELLING ({target_name}: a language written in the wrong letters) --"
        )
        for line in mangled[:40]:
            print(f"    {line}")
        if len(mangled) > 40:
            print(f"    ... +{len(mangled) - 40} more")
    if dead:
        print(f"\n  -- DEAD ({target_name}: no reference, not dynamic) --")
        for k in dead[:50]:
            print(f"    {k}")
        if len(dead) > 50:
            print(f"    ... +{len(dead) - 50} more")


def prune_locales(target: Target, dead: list[str], apply: bool) -> None:
    for path in target.locales_dir.glob("*.json"):
        with path.open(encoding="utf-8-sig") as f:
            data = json.load(f)
        removed = sum(1 for k in dead if remove_key(data, k))
        data = prune_empty(data)
        if apply:
            path.write_text(
                json.dumps(data, indent=2, ensure_ascii=False) + "\n",
                encoding="utf-8",
            )
            print(f"    {path.name}: removed {removed} keys, written")
        else:
            print(f"    {path.name}: would remove {removed} keys (dry-run)")


def check(target: Target, args) -> bool:
    en_path = target.locales_dir / "en.json"
    if not en_path.exists():
        print(f"[{target.name}] ERROR: {en_path} not found", file=sys.stderr)
        return True

    en = load_locale(en_path)
    others = {
        p.stem: load_locale(p)
        for p in target.locales_dir.glob("*.json")
        if p != en_path
    }
    refs = scan_references(target, set(en.keys()))

    dead = [
        k
        for k in sorted(en)
        if en[k] != "[]" and k not in refs and not is_dynamic(target, k)
    ]
    # An extension's web panel asks for a short key and its C# resolves that
    # inside the extension's own namespace: t('allDone') is toolkit.allDone.
    # Both spellings are the same key, so a bare reference counts as found
    # when any top-level namespace carries it.
    namespaces = {k.split(".", 1)[0] for k in en if "." in k}

    def known(ref: str) -> bool:
        return ref in en or any(f"{ns}.{ref}" in en for ns in namespaces)

    missing = sorted(r for r in refs if not known(r) and not is_dynamic(target, r))

    # Keys present in en.json but absent from a translated locale (untranslated).
    untranslated: list[str] = []
    for lang, loc in others.items():
        for k in en:
            if en[k] == "[]":
                continue
            if k not in loc:
                untranslated.append(f"{lang}::{k}")

    mangled = spelling_faults(en, others)

    drift: list[str] = []
    for lang, loc in others.items():
        for k, env in en.items():
            if k not in loc:
                continue
            if placeholders(env) != placeholders(loc[k]):
                drift.append(
                    f"{lang}::{k}  en={placeholders(env)} {lang}={placeholders(loc[k])}"
                )

    print(f"\n=== Locale doctor: {target.name} ===")
    print(f"  en.json keys:        {len(en)}")
    for lang, loc in others.items():
        print(f"  {lang}.json keys:      {len(loc)}")
    print(f"  static references:   {len(refs)}")
    print(f"  dead keys:           {len(dead)}")
    print(f"  missing keys:        {len(missing)}")
    print(f"  untranslated keys:   {len(untranslated)}")
    print(f"  placeholder drift:   {len(drift)}")
    print(f"  spelling faults:     {len(mangled)}")

    report_details(
        target.name,
        {
            "missing": missing,
            "untranslated": untranslated,
            "drift": drift,
            "mangled": mangled,
            "dead": dead,
        },
    )

    if args.prune and dead:
        print(f"\n  -- PRUNING {len(dead)} dead keys ({target.name}) --")
        prune_locales(target, dead, args.apply)

    fail = bool(missing) or bool(drift) or bool(untranslated) or bool(mangled)
    if bool(dead) and not args.no_fail_on_dead:
        fail = True
    return fail


def main() -> int:
    parser = argparse.ArgumentParser(description="Locale doctor for Novalist.")
    parser.add_argument(
        "--target", choices=["react", "desktop", "both"], default="both"
    )
    parser.add_argument("--prune", action="store_true", help="Remove dead keys.")
    parser.add_argument(
        "--apply", action="store_true", help="Actually write files (with --prune)."
    )
    parser.add_argument(
        "--no-fail-on-dead",
        action="store_true",
        help="Report dead keys but do not fail on them.",
    )
    parser.add_argument(
        "--no-fail-on-untranslated",
        action="store_true",
        help="Report untranslated keys but do not fail on them.",
    )
    args = parser.parse_args()

    targets = {"react": [REACT], "desktop": [DESKTOP], "both": [REACT, DESKTOP]}[
        args.target
    ]
    # Always, whichever target was asked for: an extension in the workspace
    # is part of what the writer runs.
    targets = targets + extension_targets()
    failed = False
    for target in targets:
        # Untranslated is a soft signal for the frozen desktop app (zh-CN is partial);
        # honor the flag globally so CI can gate the new app while tolerating desktop.
        if check(target, args) and (
            target.name == "desktop" or not args.no_fail_on_untranslated
        ):
            failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

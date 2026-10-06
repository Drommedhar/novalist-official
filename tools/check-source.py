#!/usr/bin/env python3
"""Run the source checks shared by CI and the pre-commit hook.

Run from any directory with: python tools/check-source.py
Builds, typechecking, packaging tests, coverage and E2E remain separate CI gates.
"""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHECKS = [
    (
        "Locale doctor",
        ["tools/locale-doctor.py", "--target", "react", "--no-fail-on-dead"],
    ),
    ("Manual doctor", ["tools/manual-doctor.py"]),
    ("Manual doctor tests", ["-m", "unittest", "tools/test_manual_doctor.py"]),
    ("Token doctor", ["tools/token-doctor.py"]),
    ("File doctor", ["tools/file-doctor.py"]),
    ("RPC doctor", ["tools/rpc-doctor.py", "--strict"]),
    ("Placement doctor", ["tools/placement-doctor.py"]),
    ("Bridge doctor", ["tools/bridge-doctor.py"]),
]


def main() -> int:
    failed = []
    for name, args in CHECKS:
        print(
            f"::group::{name}" if os.environ.get("GITHUB_ACTIONS") else f"\n{name}",
            flush=True,
        )
        result = subprocess.run([sys.executable, *args], cwd=ROOT, check=False)
        if os.environ.get("GITHUB_ACTIONS"):
            print("::endgroup::", flush=True)
        if result.returncode:
            failed.append(name)

    if failed:
        print(f"\nSource checks failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    print(f"\nAll {len(CHECKS)} source checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

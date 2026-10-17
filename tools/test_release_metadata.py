"""Run the release workflow's metadata step against an isolated Git remote."""

import json
import os
import shlex
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

from tools.test_changelog import CHANGELOG, PUBLISHED

ROOT = Path(__file__).resolve().parents[1]
BASH = (
    str(Path(shutil.which("git")).resolve().parents[1] / "bin/bash.exe")
    if os.name == "nt"
    else shutil.which("bash")
)


class ReleaseMetadataTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="novalist-release-metadata-")
        self.addCleanup(temporary.cleanup)
        base = Path(temporary.name)
        self.checkout = base / "checkout"
        self.checkout.mkdir()
        remote = base / "origin.git"
        self.run_command("git", "init", "--bare", "--initial-branch=main", str(remote))
        self.run_command("git", "init", "--initial-branch=main")
        self.run_command("git", "remote", "add", "origin", str(remote))
        self.run_command("git", "config", "user.name", "Release test")
        self.run_command("git", "config", "user.email", "release@example.test")
        (self.checkout / "tools").mkdir()
        shutil.copyfile(ROOT / "tools/changelog.py", self.checkout / "tools/changelog.py")
        (self.checkout / "CHANGELOG.md").write_text(CHANGELOG, encoding="utf-8")
        (self.checkout / "app").mkdir()
        package = {"name": "release-test", "version": "3.3.0", "private": True}
        self.write_json("app/package.json", package)
        self.write_json("app/package-lock.json", {
            "name": package["name"], "version": package["version"],
            "lockfileVersion": 3, "requires": True, "packages": {"": package}
        })
        self.run_command("git", "add", ".")
        self.run_command("git", "commit", "-m", "Release fixture")
        self.run_command("git", "push", "-u", "origin", "main")

    def write_json(self, name, value):
        (self.checkout / name).write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    def run_command(self, *command, env=None):
        result = subprocess.run(
            command, cwd=self.checkout, env=env, capture_output=True,
            text=True, encoding="utf-8", check=False
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout.strip()

    def stamp(self, version):
        workflow = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")
        step = workflow.split("      - name: Stamp the changelog and package version\n", 1)[1]
        self.assertIn("if: ${{ !contains(github.ref_name, '-') }}", step)
        self.assertLess(workflow.index("- name: Create GitHub release"), workflow.index(step))
        script = textwrap.dedent(step.split("        run: |\n", 1)[1])
        for key, value in {"version": version, "date": "2026-10-17", "previous_tag": "v3.5.1"}.items():
            script = script.replace("${{ steps.version.outputs." + key + " }}", value)
        script = script.replace("python3", shlex.quote(Path(sys.executable).as_posix()))
        self.run_command(BASH, "-c", script, env={**os.environ, "GITHUB_REF_NAME": "v" + version})

    def assert_metadata(self, version, package_version):
        for name in ("app/package.json", "app/package-lock.json"):
            document = json.loads(self.run_command("git", "show", "origin/main:" + name))
            self.assertEqual(document["version"], package_version)
            if "packages" in document:
                self.assertEqual(document["packages"][""]["version"], package_version)
        changelog = (self.checkout / "CHANGELOG.md").read_text(encoding="utf-8")
        self.assertIn(f"## [{version}] - 2026-10-17", changelog)
        self.assertIn(PUBLISHED.split("[Unreleased]:")[0], changelog)
        changed = self.run_command("git", "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")
        self.assertEqual(set(changed.splitlines()), {"CHANGELOG.md", "app/package.json", "app/package-lock.json"})
        self.assertEqual(self.run_command("git", "status", "--porcelain"), "")

    def test_full_version_is_committed_with_changelog_and_lockfile(self):
        self.stamp("3.6.1")
        self.assert_metadata("3.6.1", "3.6.1")

    def test_short_version_is_padded_for_npm(self):
        self.stamp("3.6")
        self.assert_metadata("3.6", "3.6.0")

    def test_reruns_do_not_consume_notes_or_downgrade_package_version(self):
        self.stamp("3.6.1")
        commit = self.run_command("git", "rev-parse", "HEAD")
        for version in ("3.6.1", "3.5.1"):
            self.stamp(version)
            self.assertEqual(self.run_command("git", "rev-parse", "HEAD"), commit)
            self.assert_metadata("3.6.1", "3.6.1")


if __name__ == "__main__":
    unittest.main()

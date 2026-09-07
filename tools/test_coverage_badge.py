"""Exercise the badge publisher against a local Git remote, without GitHub access."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "eng" / "Publish-CoverageBadge.ps1"


class CoverageBadgeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="novalist-badge-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.remote = self.root / "remote.git"
        self.seed = self.root / "seed"
        self.git("init", "--bare", str(self.remote))
        self.git("init", "--initial-branch=main", str(self.seed))
        self.git("-C", str(self.seed), "config", "user.name", "Badge test")
        self.git("-C", str(self.seed), "config", "user.email", "badge@example.invalid")
        (self.seed / "README.md").write_text("Local badge test\n", encoding="utf-8")
        self.git("-C", str(self.seed), "add", "README.md")
        self.git("-C", str(self.seed), "commit", "-m", "Seed main")
        self.git("-C", str(self.seed), "push", str(self.remote), "main")
        self.git("--git-dir", str(self.remote), "symbolic-ref", "HEAD", "refs/heads/main")

    def git(self, *args):
        return subprocess.run(
            ["git", *args], check=True, capture_output=True, text=True
        ).stdout.strip()

    def publish(self, coverage, git_config=None):
        env = os.environ.copy()
        # Rewrite the publisher's remote to an isolated local repository. Its
        # temporary clones also live here so cleanup never touches user repos.
        env.update({
            "GH_TOKEN": "local-test-token",
            "GIT_CONFIG_COUNT": "1",
            "GIT_CONFIG_KEY_0": f"url.{self.remote.as_uri()}.insteadOf",
            "GIT_CONFIG_VALUE_0": "https://x-access-token:local-test-token@github.com/ci/badge-test.git",
            "TEMP": str(self.root),
            "TMP": str(self.root),
            "TMPDIR": str(self.root),
            "NOVALIST_TEST_BADGE_SCRIPT": str(SCRIPT),
            "NOVALIST_TEST_COVERAGE": coverage,
        })
        for index, (key, value) in enumerate((git_config or {}).items(), start=1):
            env[f"GIT_CONFIG_KEY_{index}"] = key
            env[f"GIT_CONFIG_VALUE_{index}"] = str(value)
            env["GIT_CONFIG_COUNT"] = str(index + 1)
        # Match Actions' pwsh wrapper: a returned script must not leave a
        # failing native exit code behind when there was nothing to publish.
        return subprocess.run(
            ["pwsh", "-NoProfile", "-NonInteractive", "-Command",
             "$ErrorActionPreference = 'Stop'; "
             "& $env:NOVALIST_TEST_BADGE_SCRIPT -Coverage $env:NOVALIST_TEST_COVERAGE "
             "-Repository 'ci/badge-test'; "
             "if (Test-Path variable:LASTEXITCODE) { exit $LASTEXITCODE }"],
            env=env, capture_output=True, text=True,
        )

    def assert_published(self, result):
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def badge(self):
        return json.loads(self.git("--git-dir", str(self.remote), "show", "badges:coverage.json"))

    def head(self):
        return self.git("--git-dir", str(self.remote), "rev-parse", "badges")

    def test_first_publication_creates_an_orphan_badge_branch(self):
        self.assert_published(self.publish("100"))
        self.assertEqual(self.badge(), {
            "schemaVersion": 1, "label": "coverage", "message": "100%", "color": "brightgreen",
        })
        self.assertEqual(self.git("--git-dir", str(self.remote), "ls-tree", "--name-only", "badges"), "coverage.json")
        self.assertEqual(self.git("--git-dir", str(self.remote), "rev-list", "--count", "badges"), "1")

    def test_unchanged_badge_returns_success_without_another_commit(self):
        self.assert_published(self.publish("100"))
        before = self.head()
        result = self.publish("100.0")
        self.assert_published(result)
        self.assertIn("nothing to push", result.stdout)
        self.assertEqual(self.head(), before)

    def test_changed_badge_is_published(self):
        self.assert_published(self.publish("100"))
        before = self.head()
        self.assert_published(self.publish("99.2"))
        self.assertNotEqual(self.head(), before)
        self.assertEqual(self.badge()["message"], "99.2%")
        self.assertEqual(self.badge()["color"], "green")

    def test_rejected_push_fails_instead_of_reporting_success(self):
        self.assert_published(self.publish("100"))
        before = self.head()
        hook = self.remote / "hooks" / "pre-receive"
        hook.write_text("#!/bin/sh\nexit 1\n", encoding="utf-8", newline="\n")
        hook.chmod(0o755)
        result = self.publish("99")
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Could not push the coverage badge", result.stderr)
        self.assertEqual(self.head(), before)

    def test_failed_commit_is_not_treated_as_an_unchanged_badge(self):
        self.assert_published(self.publish("100"))
        before = self.head()
        hooks = self.root / "reject-commit"
        hooks.mkdir()
        hook = hooks / "pre-commit"
        hook.write_text("#!/bin/sh\nexit 1\n", encoding="utf-8", newline="\n")
        hook.chmod(0o755)
        result = self.publish("99", {"core.hooksPath": hooks.as_posix()})
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Could not commit the coverage badge", result.stderr)
        self.assertNotIn("nothing to push", result.stdout)
        self.assertEqual(self.head(), before)


if __name__ == "__main__":
    unittest.main()

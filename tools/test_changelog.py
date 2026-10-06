"""Exercise release notes and compare links through the changelog CLI."""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("changelog.py")
REPO = "https://github.com/example/novalist"
NOTES = "### Added\n\n- Interface transitions.\n"
PUBLISHED = f"""## [3.5.1] - 2026-10-03

### Fixed

- Comment highlights.

---

## [3.5] - 2026-10-02

### Added

- Scene notes.

---

[Unreleased]: {REPO}/compare/v3.5.1...HEAD
[3.5.1]: {REPO}/compare/v3.5...v3.5.1
[3.5]: {REPO}/releases/tag/v3.5
"""
CHANGELOG = f"# Changelog\n\n## [Unreleased]\n\n{NOTES}\n---\n\n{PUBLISHED}"


class ChangelogTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="novalist-changelog-")
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name) / "CHANGELOG.md"
        self.path.write_text(CHANGELOG, encoding="utf-8")

    def run_cli(self, *args):
        return subprocess.run(
            [sys.executable, str(SCRIPT), "--file", str(self.path), *args],
            cwd=self.path.parent,
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=False,
        )

    def stamp(self, *extra):
        return self.run_cli(
            "release",
            "--version",
            "3.5.3",
            "--tag",
            "v3.5.3",
            "--date",
            "2026-10-06",
            *extra,
        )

    def assert_success(self, result):
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_previous_tag_ignores_links_without_a_published_section(self):
        self.path.write_text(
            CHANGELOG.replace(
                "[Unreleased]:",
                f"[3.5.2]: {REPO}/compare/v3.5.1...v3.5.2\n[Unreleased]:",
            ),
            encoding="utf-8",
        )
        result = self.run_cli("previous-tag", "--version", "v3.5.3")
        self.assert_success(result)
        self.assertEqual(result.stdout.strip(), "v3.5.1")

    def test_stamp_uses_published_baseline_and_preserves_previous_notes(self):
        self.assert_success(self.stamp())
        stamped = self.path.read_text(encoding="utf-8")
        self.assertIn("## [3.5.3] - 2026-10-06", stamped)
        self.assertIn(f"[3.5.3]: {REPO}/compare/v3.5.1...v3.5.3", stamped)
        self.assertIn(f"[Unreleased]: {REPO}/compare/v3.5.3...HEAD", stamped)
        self.assertIn(PUBLISHED.split("[Unreleased]:")[0], stamped)
        extracted = self.run_cli("extract", "--version", "v3.5.3", "--require-content")
        self.assert_success(extracted)
        self.assertEqual(extracted.stdout, NOTES)

    def test_rerun_keeps_history_and_uses_the_original_previous_release(self):
        self.assert_success(self.stamp())
        newer = self.path.read_text(encoding="utf-8").replace(
            "## [3.5.3]", "## [3.6] - 2026-10-07\n\n- Later feature.\n\n## [3.5.3]"
        )
        self.path.write_text(newer, encoding="utf-8")
        result = self.run_cli("previous-tag", "--version", "3.5.3")
        self.assert_success(result)
        self.assertEqual(result.stdout.strip(), "v3.5.1")
        result = self.stamp()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("already released", result.stderr)
        self.assertEqual(self.path.read_text(encoding="utf-8"), newer)
        self.assertEqual(self.run_cli("extract", "--version", "3.5.3").stdout, NOTES)

    def test_extract_unreleased_and_last_release_omit_links_and_separators(self):
        for arguments, expected in (
            (("--unreleased",), NOTES),
            (("--version", "v3.5"), "### Added\n\n- Scene notes.\n"),
        ):
            with self.subTest(arguments=arguments):
                result = self.run_cli("extract", *arguments, "--require-content")
                self.assert_success(result)
                self.assertEqual(result.stdout, expected)

    def test_first_release_has_no_previous_tag_and_links_to_its_release_page(self):
        self.path.write_text(
            f"## [Unreleased]\n\n{NOTES}\n[Unreleased]: {REPO}/releases/tag/v0.0.0\n",
            encoding="utf-8",
        )
        result = self.run_cli("previous-tag", "--version", "3.5.3")
        self.assert_success(result)
        self.assertEqual(result.stdout, "")
        self.assert_success(self.stamp())
        self.assertIn(
            f"[3.5.3]: {REPO}/releases/tag/v3.5.3",
            self.path.read_text(encoding="utf-8"),
        )

    def test_previous_tag_reads_release_page_links(self):
        result = self.run_cli("previous-tag", "--version", "v3.5.1")
        self.assert_success(result)
        self.assertEqual(result.stdout.strip(), "v3.5")

    def test_missing_previous_link_fails_without_stamping(self):
        damaged = CHANGELOG.replace(f"[3.5.1]: {REPO}/compare/v3.5...v3.5.1\n", "")
        self.path.write_text(damaged, encoding="utf-8")
        for command in (
            lambda: self.run_cli("previous-tag", "--version", "3.5.3"),
            self.stamp,
        ):
            result = command()
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("no release tag link for '3.5.1'", result.stderr)
            self.assertEqual(self.path.read_text(encoding="utf-8"), damaged)

    def test_explicit_previous_tag_override_is_preserved(self):
        self.assert_success(self.stamp("--previous-tag", "desktop-v3.5.1"))
        self.assertIn(
            f"[3.5.3]: {REPO}/compare/desktop-v3.5.1...v3.5.3",
            self.path.read_text(encoding="utf-8"),
        )


if __name__ == "__main__":
    unittest.main()

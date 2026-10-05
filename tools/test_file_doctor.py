"""Keep JavaScript assets protected after inline HTML scripts are extracted."""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


DOCTOR = Path(__file__).resolve().with_name("file-doctor.py")
JAVASCRIPT_SUFFIXES = (".js", ".mjs", ".cjs", ".jsx")
SCRIPT = "\n".join(f"const value{number} = {number};" for number in range(100)) + "\n"


class FileDoctorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.git("init", "--quiet")

    def git(self, *arguments: str) -> None:
        subprocess.run(
            ["git", *arguments], cwd=self.root, check=True, capture_output=True, text=True
        )

    def commit(self) -> None:
        self.git("add", ".")
        self.git(
            "-c", "user.name=File doctor fixture",
            "-c", "user.email=file-doctor@example.invalid",
            "-c", "commit.gpgsign=false",
            "-c", f"core.hooksPath={self.root / '.disabled-hooks'}",
            "commit", "--quiet", "-m", "Record fixture",
        )

    def doctor(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(DOCTOR)], cwd=self.root, capture_output=True, text=True
        )

    def test_committed_extracted_script_remains_protected(self) -> None:
        entry = self.root / "map.html"
        entry.write_text("<script>\n" + SCRIPT + "</script>\n", encoding="utf-8")
        self.commit()

        script = self.root / "map.js"
        script.write_text(SCRIPT, encoding="utf-8")
        entry.write_text('<script src="map.js"></script>\n', encoding="utf-8")
        self.git("add", ".")
        extracted = self.doctor()
        self.assertEqual(0, extracted.returncode, extracted.stdout + extracted.stderr)
        self.commit()

        script.write_text("const value0 = 0;\n", encoding="utf-8")
        truncated = self.doctor()
        self.assertEqual(1, truncated.returncode, truncated.stdout + truncated.stderr)
        self.assertIn("map.js: 100 lines -> 1", truncated.stdout)

    def test_empty_javascript_sources_are_reported(self) -> None:
        paths = [self.root / ("asset" + suffix) for suffix in JAVASCRIPT_SUFFIXES]
        for path in paths:
            path.write_text(SCRIPT, encoding="utf-8")
        self.commit()
        for path in paths:
            path.write_text("", encoding="utf-8")

        result = self.doctor()
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertIn("Tracked source files that are empty:", result.stdout)
        for path in paths:
            self.assertIn(path.name, result.stdout)

    def test_gutted_javascript_sources_are_reported(self) -> None:
        paths = [self.root / ("asset" + suffix) for suffix in JAVASCRIPT_SUFFIXES]
        for path in paths:
            path.write_text(SCRIPT, encoding="utf-8")
        self.commit()
        for path in paths:
            path.write_text("const value0 = 0;\n", encoding="utf-8")

        result = self.doctor()
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        for path in paths:
            self.assertIn(f"{path.name}: 100 lines -> 1", result.stdout)


if __name__ == "__main__":
    unittest.main()

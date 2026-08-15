from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent
MODULE_PATH = TOOLS_DIR / "manual-doctor.py"
SPEC = importlib.util.spec_from_file_location("manual_doctor", MODULE_PATH)
assert SPEC and SPEC.loader
manual_doctor = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = manual_doctor
SPEC.loader.exec_module(manual_doctor)


class ManualDoctorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.manual = Path(self.temporary.name) / "docs" / "manual"
        self.manual.mkdir(parents=True)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def write(self, relative: str, content: str = "") -> None:
        path = self.manual / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def messages(self) -> list[str]:
        return [problem.message for problem in manual_doctor.check_manual(self.manual)]

    def test_accepts_pages_fragments_images_and_duplicate_headings(self) -> None:
        self.write(
            "README.md",
            "# Manual\n\n[First](01-first.md)\n\n[Second](02-second.md)\n",
        )
        self.write(
            "01-first.md",
            "# First\n\n[Details](#details)\n\n"
            "![Diagram](images/diagram.png)\n\n"
            "## Details\n\n[Other](02-second.md#repeated-1)\n",
        )
        self.write("02-second.md", "# Second\n\n## Repeated\n\n## Repeated\n")
        self.write("images/diagram.png", "not-a-real-png")

        self.assertEqual([], self.messages())

    def test_reports_missing_page_and_heading(self) -> None:
        self.write("README.md", "# Manual\n\n[First](01-first.md)\n")
        self.write(
            "01-first.md",
            "# First\n\n[Gone](02-gone.md)\n\n[Wrong](#not-here)\n",
        )

        messages = self.messages()
        self.assertTrue(any("missing manual page" in message for message in messages))
        self.assertTrue(any("missing heading '#not-here'" in message for message in messages))

    def test_reports_missing_image(self) -> None:
        self.write("README.md", "# Manual\n\n[First](01-first.md)\n")
        self.write("01-first.md", "# First\n\n![Gone](images/gone.png)\n")

        self.assertTrue(any("missing image" in message for message in self.messages()))

    def test_ignores_link_examples_in_inline_and_fenced_code(self) -> None:
        self.write("README.md", "# Manual\n\n[First](01-first.md)\n")
        self.write(
            "01-first.md",
            "# First\n\n"
            "Type `![name](images/example.png)` to add an image.\n\n"
            "```markdown\n[example](missing.md#gone)\n```\n",
        )

        self.assertEqual([], self.messages())

    def test_reports_page_missing_from_table_of_contents(self) -> None:
        self.write("README.md", "# Manual\n\n[First](01-first.md)\n")
        self.write("01-first.md", "# First\n")
        self.write("02-hidden.md", "# Hidden\n")

        self.assertIn(
            "page is missing from README.md table of contents",
            self.messages(),
        )


if __name__ == "__main__":
    unittest.main()

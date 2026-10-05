"""Ensure source checks follow deployed classic scripts, not unrelated files."""

import tempfile
import unittest
from pathlib import Path

from tools.html_sources import classic_script_paths, extracted_script_lines


class HtmlSourcesTests(unittest.TestCase):
    def test_truncation_check_credits_only_verbatim_referenced_extractions(self):
        with tempfile.TemporaryDirectory() as folder:
            entry = Path(folder) / "editor.html"
            script = Path(folder) / "editor.js"
            body = "\nfunction bridge() {\n  return 1;\n}\n"
            previous = "<script>" + body + "</script>"
            entry.write_text('<script src="editor.js"></script>', encoding="utf-8")
            script.write_text(body, encoding="utf-8")
            self.assertEqual(body.count("\n"), extracted_script_lines(previous, entry))
            self.assertEqual(body.count("\n"), extracted_script_lines(previous + previous, entry))
            script.write_text("function unrelated() {}", encoding="utf-8")
            self.assertEqual(0, extracted_script_lines(previous, entry))
            script.unlink()
            self.assertEqual(0, extracted_script_lines(previous, entry))

    def test_tracks_local_classic_scripts_and_preserves_missing_references(self):
        with tempfile.TemporaryDirectory() as folder:
            entry = Path(folder) / "editor.html"
            entry.write_text('''
                <script>function inlineBridge() {}</script>
                <script src="editor.js?v=2"></script>
                <script src="editor.js?v=3"></script>
                <script type="text/javascript" src="rich%20text.js"></script>
                <script type="module" src="private.js"></script>
                <script src="https://example.com/remote.js"></script>
                <script src="//example.com/remote.js"></script>
                <script type="importmap">{}</script>
            ''', encoding="utf-8")
            self.assertEqual(
                [entry, (entry.parent / "editor.js").resolve(), (entry.parent / "rich text.js").resolve()],
                classic_script_paths(entry),
            )
            with self.assertRaises(FileNotFoundError):
                classic_script_paths(entry)[1].read_text(encoding="utf-8")


if __name__ == "__main__":
    unittest.main()

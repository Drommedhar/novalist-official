"""Command scopes must not leak between registry modules."""

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS_DIR))
SPEC = importlib.util.spec_from_file_location(
    "placement_doctor", TOOLS_DIR / "placement-doctor.py"
)
assert SPEC and SPEC.loader
placement_doctor = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = placement_doctor
SPEC.loader.exec_module(placement_doctor)


class PlacementDoctorTests(unittest.TestCase):
    def test_generated_family_does_not_change_previous_module_scope(self):
        with tempfile.TemporaryDirectory() as folder:
            registry = Path(folder) / "commands.ts"
            registry.write_text(
                "import { EDITOR_COMMANDS } from './editorCommands'\n"
                "const NAV_VIEWS = ['editor']\n"
                "const COMMANDS = [{\n"
                "  id: 'app.about',\n"
                "  scope: 'application',\n"
                "}]\n",
                encoding="utf-8",
            )
            registry.with_name("editorCommands.ts").write_text(
                "const PARAGRAPH_STYLES = ['', 'heading']\n"
                "const GENERATED = PARAGRAPH_STYLES.map(style => ({\n"
                "  id: `paragraph.style.${style}`,\n"
                "  scope: 'paragraph',\n"
                "}))\n"
                "const EDITOR_COMMANDS = [{\n"
                "  id: 'text.bold',\n"
                "  scope: 'selection',\n"
                "  home: 'contextMenu',\n"
                "}]\n",
                encoding="utf-8",
            )
            commands = placement_doctor.parse_registry(
                placement_doctor.registry_sources(registry),
                {
                    "application": "menuBar",
                    "paragraph": "viewBar",
                    "selection": "selectionBar",
                },
            )
            self.assertEqual(
                [(command.id, command.home) for command in commands],
                [
                    ("app.about", "menuBar"),
                    ("text.bold", "contextMenu"),
                    ("nav.editor", "menuBar"),
                    ("paragraph.style.body", "viewBar"),
                    ("paragraph.style.heading", "viewBar"),
                ],
            )
            self.assertTrue(commands[1].declared_home)


if __name__ == "__main__":
    unittest.main()

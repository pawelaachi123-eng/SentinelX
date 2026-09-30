import importlib.util
import json
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "create_mobile_app.py"
SPEC = importlib.util.spec_from_file_location("create_mobile_app", SCRIPT)
assert SPEC and SPEC.loader
GENERATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GENERATOR)


class MobileAppGeneratorTests(unittest.TestCase):
    def test_all_reviewed_templates_generate_bounded_projects(self):
        with tempfile.TemporaryDirectory() as temp:
            for template in GENERATOR.TEMPLATES:
                with self.subTest(template=template):
                    output = Path(temp) / template
                    GENERATOR.create_app(template, "Moja Aplikacja", output)
                    metadata = json.loads((output / "sentinel-app.json").read_text(encoding="utf-8"))
                    self.assertEqual(metadata["template"], template)
                    self.assertFalse(metadata["networkPermission"])
                    self.assertEqual(metadata["applicationId"], f"com.sentinelx.generated.{template}")
                    self.assertIn(metadata["applicationId"], (output / "app/build.gradle.kts").read_text(encoding="utf-8"))
                    self.assertTrue((output / "app/src/main/java/com/sentinelx/generated/MainActivity.kt").is_file())
                    manifest = ET.parse(output / "app/src/main/AndroidManifest.xml").getroot()
                    android = "{http://schemas.android.com/apk/res/android}"
                    self.assertIsNone(manifest.find("uses-permission"))
                    self.assertEqual(manifest.find("application").get(android + "label"), "@string/app_name")
                    self.assertIn("Moja Aplikacja", (output / "app/src/main/res/values/strings.xml").read_text(encoding="utf-8"))
                    self.assertTrue((output / "README.md").is_file())

    def test_rejects_injection_and_does_not_overwrite(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError):
                GENERATOR.create_app("counter", 'App"/><uses-permission', Path(temp) / "bad")
            occupied = Path(temp) / "occupied"
            occupied.mkdir()
            sentinel = occupied / "keep.txt"
            sentinel.write_text("keep", encoding="utf-8")
            with self.assertRaises(FileExistsError):
                GENERATOR.create_app("notes", "Notes", occupied)
            self.assertEqual(sentinel.read_text(encoding="utf-8"), "keep")

    def test_unknown_template_is_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError):
                GENERATOR.create_app("arbitrary-code", "App", Path(temp) / "out")


if __name__ == "__main__":
    unittest.main()

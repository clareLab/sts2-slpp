import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import workshop


class WorkshopTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        for name in ("src", "workshop", "artifacts/dist/slpp"):
            (self.root / name).mkdir(parents=True)
        self.config = {"id": None, "visibility": "public", "dependencies": [3737335127]}
        self.manifest = {"id": "slpp", "name": "Save & Load ++", "description": "Undo and redo.", "version": "0.3.0"}
        self.save_config()
        workshop.write_json(self.root / "src/slpp.json", self.manifest)
        workshop.write_json(self.root / "artifacts/dist/slpp/slpp.json", self.manifest)
        (self.root / "src/slpp.csproj").write_text("<Project><PropertyGroup><Version>0.3.0</Version></PropertyGroup></Project>")
        (self.root / "workshop/image.png").write_bytes(b"\x89PNG\r\n\x1a\n")
        (self.root / "artifacts/dist/slpp/slpp.dll").write_bytes(b"fixture")
        (self.root / "artifacts/dist/slpp/LICENSE").write_text("MIT")

    def save_config(self):
        workshop.write_json(self.root / "workshop/config.json", self.config)

    def test_only_release_files_and_dependency_are_uploaded(self):
        (self.root / "artifacts/dist/slpp/BaseLib.dll").write_bytes(b"must not ship")
        workspace = workshop.prepare(self.root)
        self.assertEqual({p.name for p in (workspace / "content/slpp").iterdir()}, {"slpp.dll", "slpp.json", "LICENSE"})
        metadata = json.loads((workspace / "workshop.json").read_text())
        self.assertEqual(metadata["title"], "Save & Load ++")
        self.assertEqual(metadata["dependencies"], [3737335127])
        self.assertEqual(metadata["changeNote"], "Version 0.3.0")

    def test_stale_package_is_rejected(self):
        workshop.write_json(self.root / "artifacts/dist/slpp/slpp.json", {**self.manifest, "version": "0.2.0"})
        with self.assertRaisesRegex(ValueError, "stale"):
            workshop.prepare(self.root)

    def test_version_mismatch_is_rejected(self):
        workshop.write_json(self.root / "src/slpp.json", {**self.manifest, "version": "0.4.0"})
        with self.assertRaisesRegex(ValueError, "Versions"):
            workshop.prepare(self.root)

    def test_large_preview_is_rejected(self):
        (self.root / "workshop/image.png").write_bytes(b"x" * 1_000_000)
        with self.assertRaisesRegex(ValueError, "1 MB"):
            workshop.prepare(self.root)

    def test_missing_preview_is_rejected(self):
        (self.root / "workshop/image.png").unlink()
        with self.assertRaisesRegex(ValueError, "must exist"):
            workshop.prepare(self.root)

    def test_existing_item_is_updated(self):
        self.config["id"] = 123
        self.save_config()
        workspace = workshop.prepare(self.root)
        self.assertEqual((workspace / "mod_id.txt").read_text(), "123\n")
        workshop.prepare(self.root)
        self.assertEqual(workshop.read_config(self.root)["id"], 123)

    def test_created_id_survives_failed_upload(self):
        workspace = workshop.prepare(self.root)
        (workspace / "mod-uploader.log").write_text("Uploading 'Save & Load ++' to the steam workshop with item ID 456...\nUpload failed\n")
        workshop.record(self.root)
        self.assertEqual(workshop.read_config(self.root)["id"], 456)
        workshop.prepare(self.root)
        self.assertEqual((workspace / "mod_id.txt").read_text(), "456\n")

    def test_different_item_cannot_replace_configured_id(self):
        self.config["id"] = 123
        self.save_config()
        workspace = workshop.prepare(self.root)
        (workspace / "mod_id.txt").unlink()
        (workspace / "mod-uploader.log").write_text("Uploading 'Save & Load ++' with item ID 456...")
        with self.assertRaisesRegex(ValueError, "different"):
            workshop.record(self.root)
        self.assertEqual(workshop.read_config(self.root)["id"], 123)

    def test_orphaned_id_prevents_duplicate_creation(self):
        workspace = workshop.prepare(self.root)
        (workspace / "mod_id.txt").write_text("123\n")
        with self.assertRaisesRegex(ValueError, "differs"):
            workshop.prepare(self.root)

    def test_conflicting_uploader_ids_are_rejected(self):
        workspace = workshop.prepare(self.root)
        (workspace / "mod_id.txt").write_text("123\n")
        (workspace / "mod-uploader.log").write_text("Uploading 'Save & Load ++' with item ID 456...")
        with self.assertRaisesRegex(ValueError, "conflicting"):
            workshop.record(self.root)
        self.assertIsNone(workshop.read_config(self.root)["id"])

    def test_invalid_item_id_is_rejected(self):
        self.config["id"] = 0
        self.save_config()
        with self.assertRaisesRegex(ValueError, "Invalid Workshop item ID"):
            workshop.prepare(self.root)

    def test_version_updates_project_and_manifest(self):
        workshop.set_version(self.root, "0.3.1")
        self.assertEqual(json.loads((self.root / "src/slpp.json").read_text())["version"], "0.3.1")
        self.assertIn("<Version>0.3.1</Version>", (self.root / "src/slpp.csproj").read_text())

    def test_invalid_version_does_not_modify_source(self):
        before = (self.root / "src/slpp.csproj").read_text()
        with self.assertRaises(ValueError):
            workshop.set_version(self.root, "0.3.1; arbitrary-command")
        self.assertEqual((self.root / "src/slpp.csproj").read_text(), before)
        self.assertEqual(json.loads((self.root / "src/slpp.json").read_text()), self.manifest)

    def test_missing_version_does_not_modify_manifest(self):
        (self.root / "src/slpp.csproj").write_text("<Project />")
        with self.assertRaisesRegex(ValueError, "one Version"):
            workshop.set_version(self.root, "0.3.1")
        self.assertEqual(json.loads((self.root / "src/slpp.json").read_text()), self.manifest)


if __name__ == "__main__":
    unittest.main()

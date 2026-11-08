"""Safety checks for the large-asset restore and repack workflow."""

import hashlib
from pathlib import Path
import tempfile
import unittest

import large_assets


class LargeAssetTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.name = "Unity/OpenTerritory/Assets/Scenes/Example.unity"
        self.source = self.root / self.name
        self.source.parent.mkdir(parents=True)
        self.original = b"original scene data\n" * 10000
        self.source.write_bytes(self.original)
        self.entry = large_assets.archive_file(self.root, self.source, self.name)
        self.manifest = {"version": 1, "chunk_size": large_assets.CHUNK_SIZE,
                         "files": [self.entry]}
        large_assets.write_manifest(self.root, self.manifest)

    def test_restore_is_exact_and_never_overwrites_a_different_file(self):
        destination = self.root / "clean-clone"
        self.assertEqual(large_assets.restore_entry(self.root, destination, self.entry), "restored")
        restored = destination / self.name
        self.assertEqual(restored.read_bytes(), self.original)
        restored.write_bytes(b"local unsaved work")
        self.assertEqual(large_assets.restore_entry(self.root, destination, self.entry), "different file kept")
        self.assertEqual(restored.read_bytes(), b"local unsaved work")

    def test_corrupt_chunk_does_not_create_a_partial_asset(self):
        chunk = self.root / large_assets.ARCHIVE / "chunks" / (self.entry["chunks"][0]["sha256"] + ".gz")
        chunk.write_bytes(b"corrupted")
        destination = self.root / "bad-clone"
        with self.assertRaises(ValueError):
            large_assets.restore_entry(self.root, destination, self.entry)
        self.assertFalse((destination / self.name).exists())
        self.assertEqual(list((destination / self.name).parent.glob(".large-asset-restore-*")), [])

    def test_pack_records_selected_changes_and_keeps_previous_chunks(self):
        self.source.write_bytes(b"new scene data")
        self.assertEqual(large_assets.inspect_source(self.root, self.entry), "changed")
        large_assets.pack(self.root, self.manifest, [self.entry])
        saved = large_assets.read_manifest(self.root)["files"][0]
        self.assertEqual(saved["sha256"], hashlib.sha256(b"new scene data").hexdigest())
        self.assertTrue(list((self.root / large_assets.ARCHIVE / ".previous").rglob("*.gz")))
        destination = self.root / "new-clone"
        self.assertEqual(large_assets.restore_entry(self.root, destination, saved), "restored")
        self.assertEqual((destination / self.name).read_bytes(), b"new scene data")

    def test_rejects_paths_outside_the_project(self):
        for path in ("../escape", "/absolute", "C:/outside", "folder\\file"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                large_assets.safe_path(self.root, path)


if __name__ == "__main__":
    unittest.main()

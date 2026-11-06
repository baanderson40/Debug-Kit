"""Tests verifier failure gates without building, loading Dalamud, or using the network."""
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import verify


class VerifierTests(unittest.TestCase):
    def setUp(self):
        approved = Path("/tmp/opencode")
        self.directory = tempfile.TemporaryDirectory(prefix="debugkit-verify-", dir=approved if approved.is_dir() else None)
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        (self.root / "DebugKit/bin/Debug").mkdir(parents=True)
        (self.root / "Directory.Build.props").write_text("<Project><PropertyGroup><Version>0.5.0</Version></PropertyGroup></Project>")
        self.source = {"InternalName": "DebugKit", "AssemblyVersion": "0.5.0.0", "DalamudApiLevel": 15, "Description": "test"}
        self.write_manifests(self.source)

    def write_manifests(self, output):
        (self.root / "DebugKit/DebugKit.json").write_text(json.dumps(self.source))
        (self.root / "DebugKit/bin/Debug/DebugKit.json").write_text(json.dumps(output))

    def test_manifest_allows_sdk_added_fields(self):
        self.write_manifests(self.source | {"LoadRequiredState": 0})
        self.assertEqual(verify.manifests(self.root), "0.5.0.0")

    def test_manifest_rejects_wrong_version_name_api_or_source_fields(self):
        for key, value in (("AssemblyVersion", "0.4.2.0"), ("InternalName", "Other"), ("DalamudApiLevel", 16), ("Description", "stale")):
            with self.subTest(key=key):
                self.write_manifests(self.source | {key: value})
                with self.assertRaises(ValueError):
                    verify.manifests(self.root)

    def test_invalid_project_version_rejected(self):
        (self.root / "Directory.Build.props").write_text("<Project><PropertyGroup><Version>not-a-version</Version></PropertyGroup></Project>")
        with self.assertRaises(ValueError):
            verify.manifests(self.root)

    def test_artifact_checks_pe_header_and_missing_file(self):
        with self.assertRaises(ValueError):
            verify.artifacts(self.root)
        header = bytearray(128)
        header[:2] = b"MZ"
        header[60:64] = (64).to_bytes(4, "little")
        header[64:68] = b"PE\0\0"
        for name in ("DebugKit.dll", "DebugKit.Core.dll"):
            (self.root / "DebugKit/bin/Debug" / name).write_bytes(header)
        with redirect_stdout(io.StringIO()):
            verify.artifacts(self.root)
        header[60:64] = (100000).to_bytes(4, "little")
        (self.root / "DebugKit/bin/Debug/DebugKit.dll").write_bytes(header)
        with self.assertRaises(ValueError):
            verify.artifacts(self.root)

    def test_documentation_checks_only_local_files_and_rejects_escapes(self):
        (self.root / "docs").mkdir()
        (self.root / "docs/test.md").write_text("# Test")
        (self.root / "AGENTS.md").write_text("instructions")
        readme = self.root / "README.md"
        readme.write_text("[valid](docs/test.md#test) [external](https://example.invalid/) [anchor](#local)")
        with redirect_stdout(io.StringIO()):
            verify.documentation(self.root)
        for text in ("[missing](docs/absent.md)", "[escape](../outside.md)"):
            readme.write_text(text)
            with self.assertRaises(ValueError):
                verify.documentation(self.root)

    def test_failed_command_is_not_reported_as_success(self):
        result = subprocess.CompletedProcess(["test"], 7, stdout="synthetic failure\n")
        with patch.object(verify.subprocess, "run", return_value=result), redirect_stdout(io.StringIO()):
            with self.assertRaises(ValueError):
                verify.run("synthetic", ["test"])


if __name__ == "__main__":
    unittest.main()

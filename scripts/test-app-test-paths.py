"""Offline path-policy checks; fake executable files are never run."""
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from app_test_paths import application, add_sdk_path


class Paths(unittest.TestCase):
    def test_default_and_override_locations(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory);exe = root / '7-Zip/7zFM.exe';exe.parent.mkdir();exe.write_bytes(b'not executable')
            with patch.dict(os.environ, {'ProgramFiles': str(root), 'VIAVW_7ZIP_EXE': ''}):
                self.assertEqual(application('VIAVW_7ZIP_EXE', '7-Zip/7zFM.exe'), str(exe.resolve()))
            with patch.dict(os.environ, {'VIAVW_7ZIP_EXE': str(exe)}):
                self.assertEqual(application('VIAVW_7ZIP_EXE', '7-Zip/7zFM.exe'), str(exe.resolve()))

    def test_bad_override_does_not_fallback(self):
        with patch.dict(os.environ, {'VIAVW_7ZIP_EXE': 'relative/7zFM.exe'}):
            with self.assertRaises(FileNotFoundError):
                application('VIAVW_7ZIP_EXE', '7-Zip/7zFM.exe')

    def test_wrong_executable_name_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'different.exe';path.touch()
            with patch.dict(os.environ, {'VIAVW_7ZIP_EXE': str(path)}):
                with self.assertRaises(ValueError):
                    application('VIAVW_7ZIP_EXE', '7-Zip/7zFM.exe')

    def test_sdk_override_is_explicit(self):
        with patch.dict(os.environ, {'VIAVW_TEST_SDK': 'missing-relative'}):
            with self.assertRaises(FileNotFoundError):
                add_sdk_path(Path.cwd())

    def test_existing_venv_needs_no_artifact_directory(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {'VIAVW_TEST_SDK': ''}):
            with patch('app_test_paths.site.addsitedir') as add:
                add_sdk_path(Path(directory))
                add.assert_not_called()


if __name__ == '__main__':
    unittest.main()

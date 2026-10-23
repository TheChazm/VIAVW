"""Focused notice checks; no build, installer or application is launched."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import zipfile
from installer_inputs import include_project_license

ROOT=Path(__file__).resolve().parents[1]


class Notices(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.project=self.root/'project';self.project.mkdir()
        self.notice=(ROOT/'LICENSE').read_bytes();(self.project/'LICENSE').write_bytes(self.notice)
        self.payload=self.root/'payload';self.payload.mkdir()
        third=self.payload/'licenses/runtime-notice.txt';third.parent.mkdir();third.write_bytes(b'unchanged third-party terms')

    def test_approved_credit_and_full_notice(self):
        text=self.notice.decode('utf-8')
        self.assertEqual(text.splitlines()[2],'Copyright (c) 2026 The Coded Craftsman, LLC')
        self.assertIn('Permission is hereby granted, free of charge',text)
        self.assertIn('THE SOFTWARE IS PROVIDED',text)
        self.assertTrue(text.rstrip().endswith('SOFTWARE.'))

    def test_msi_payload_adds_exact_project_license(self):
        digest=include_project_license(self.project,self.payload)
        self.assertEqual((self.payload/'LICENSE').read_bytes(),self.notice)
        self.assertEqual(digest,hashlib.sha256(self.notice).hexdigest().upper())
        self.assertEqual((self.payload/'licenses/runtime-notice.txt').read_bytes(),b'unchanged third-party terms')
        # MSI authoring enumerates the resulting payload; no MSI is generated.
        self.assertIn('LICENSE',{p.relative_to(self.payload).as_posix() for p in self.payload.rglob('*') if p.is_file()})

    def test_same_notice_retained_and_different_notice_preserved(self):
        (self.payload/'LICENSE').write_bytes(self.notice)
        include_project_license(self.project,self.payload)
        (self.payload/'LICENSE').write_bytes(b'other notice')
        with self.assertRaises(ValueError):include_project_license(self.project,self.payload)
        self.assertEqual((self.payload/'LICENSE').read_bytes(),b'other notice')

    def test_missing_source_notice_fails(self):
        (self.project/'LICENSE').unlink()
        with self.assertRaises(FileNotFoundError):include_project_license(self.project,self.payload)

    def test_msi_notice_precedes_payload_enumeration(self):
        text=(ROOT/'scripts/build-windows-installer.py').read_text()
        self.assertLess(text.index('project_license_sha256=include_project_license(ROOT,payload)'),text.index("for file in sorted(payload.rglob('*'))"))

    def test_both_zip_modes_hash_and_include_project_notice(self):
        powershell=shutil.which('pwsh')
        if not powershell:self.skipTest('PowerShell 7 is required for package-copy validation')
        for name in ('build-standalone-tool.ps1','build-tracked-package.ps1'):
            with self.subTest(script=name):
                source=(ROOT/'scripts'/name).read_text()
                lines=[line.strip() for line in source.splitlines() if "Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE')" in line]
                self.assertEqual(len(lines),1)
                start=source.index('$hashes = [ordered]@{}');end=source.index('$package = [ordered]@{',start)
                self.assertLess(source.index(lines[0]),start)
                destination=self.root/name;destination.mkdir()
                (destination/'licenses').mkdir();(destination/'licenses/runtime-notice.txt').write_bytes(b'unchanged third-party terms')
                env=dict(os.environ,VIAVW_NOTICE_SOURCE=str(self.project),VIAVW_NOTICE_OUTPUT=str(destination))
                snippet="$ErrorActionPreference='Stop';$projectRoot=$env:VIAVW_NOTICE_SOURCE;$destination=$env:VIAVW_NOTICE_OUTPUT;"+lines[0]+';'+source[start:end]+";$hashes | ConvertTo-Json"
                result=subprocess.run([powershell,'-NoProfile','-NonInteractive','-Command',snippet],env=env,capture_output=True,text=True,check=True)
                files=json.loads(result.stdout)
                self.assertEqual(files['LICENSE'],hashlib.sha256(self.notice).hexdigest().upper())
                archive=self.root/(name+'.zip')
                with zipfile.ZipFile(archive,'x') as zipped:
                    for filename in files:zipped.write(destination/filename,filename)
                with zipfile.ZipFile(archive) as zipped:
                    self.assertEqual(zipped.read('LICENSE'),self.notice)
                    self.assertEqual(zipped.read('licenses/runtime-notice.txt'),b'unchanged third-party terms')
        selector=(ROOT/'scripts/package-standalone-tool.py').read_text()
        self.assertIn("for name in [*manifest['files'],'package.json']",selector)


if __name__=='__main__':unittest.main()

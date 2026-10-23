"""Offline installer input tests; synthetic files are never executed."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from installer_inputs import HOST, REQUIRED, digest, existing_tool, member, metadata, validate_archive, validate_package


class Inputs(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.files={name:('synthetic '+name).encode() for name in REQUIRED}
        self.manifest=dict(schema=1,version='0.1.2',release='tool-unit-test',platform='win-x64',selfContained=True,
            files={name:hashlib.sha256(data).hexdigest().upper() for name,data in self.files.items()})
        self.manifest['hostAssemblySha256']=self.manifest['files'][HOST+'LayerHost.dll']

    def raw(self):return json.dumps(self.manifest).encode()

    def archive(self, extra=None):
        path=self.root/'input.zip'
        with zipfile.ZipFile(path,'w') as archive:
            for name,data in self.files.items():archive.writestr(name,data)
            archive.writestr('package.json',self.raw())
            if extra:archive.writestr(*extra)
        return path

    def test_valid_archive_materializes_only_manifest(self):
        path=self.archive();validated=validate_archive(path,digest(path),'0.1.2')
        output=self.root/'payload';validated.materialize(output)
        self.assertEqual({p.relative_to(output).as_posix() for p in output.rglob('*') if p.is_file()},set(self.files)|{'package.json'})

    def test_hash_is_required_and_must_match(self):
        path=self.archive()
        for value in (None,'0'*64):
            with self.assertRaises(ValueError):validate_archive(path,value)

    def test_requested_version_must_agree(self):
        path=self.archive()
        with self.assertRaises(ValueError):validate_archive(path,digest(path),'0.1.3')

    def test_msi_version_limits_and_platform(self):
        for value in ('1.2.3-beta','256.1.0','1.256.0','1.2.65536','01.2.3'):
            self.manifest['version']=value
            with self.assertRaises(ValueError):metadata(self.raw())
        self.manifest['version']='0.1.2';self.manifest['platform']='linux-x64'
        with self.assertRaises(ValueError):metadata(self.raw())

    def test_path_traversal_absolute_and_windows_names(self):
        for name in ('../escape','/root','C:/file','a\\b','a//b','a/CON.txt','a/end.','a/end ','a/question?.dll','a/star*.dll'):
            with self.assertRaises(ValueError):member(name)

    def test_extra_archive_file_rejected(self):
        path=self.archive(('../escape',b'bad'))
        with self.assertRaises(ValueError):validate_archive(path,digest(path))
        path=self.archive(('unmanifested.txt',b'bad'))
        with self.assertRaises(ValueError):validate_archive(path,digest(path))

    def test_case_collision_rejected(self):
        path=self.archive(('VIAVW.EXE',b'collision'))
        with self.assertRaises(ValueError):validate_archive(path,digest(path))

    def test_tampered_member_rejected(self):
        self.files['viavw.dll']=b'tampered'
        path=self.archive()
        with self.assertRaises(ValueError):validate_archive(path,digest(path))

    def test_missing_manager_and_bad_host_metadata(self):
        del self.manifest['files'][HOST+'VIAVW.Manager.exe']
        with self.assertRaises(ValueError):metadata(self.raw())
        self.manifest['files'][HOST+'VIAVW.Manager.exe']=hashlib.sha256(b'manager').hexdigest()
        self.manifest['hostAssemblySha256']='0'*64
        with self.assertRaises(ValueError):metadata(self.raw())

    def test_package_hash_and_extra_runtime_state(self):
        package=self.root/'package';package.mkdir()
        for name,data in self.files.items():
            target=package/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
        (package/'package.json').write_bytes(self.raw())
        value=digest(package/'package.json')
        self.assertEqual(validate_package(package,value).manifest['version'],'0.1.2')
        with self.assertRaises(ValueError):validate_package(package,'0'*64)
        (package/'runtime-state.json').write_text('{}')
        with self.assertRaises(ValueError):validate_package(package,value)

    def test_missing_tool_fails_without_installation(self):
        with self.assertRaises(FileNotFoundError):existing_tool(self.root/'missing.exe','WiX')

    def test_changed_archive_and_existing_output_rejected(self):
        path=self.archive();validated=validate_archive(path,digest(path))
        path.write_bytes(path.read_bytes()+b'changed')
        with self.assertRaises(ValueError):validated.materialize(self.root/'payload')
        with self.assertRaises(ValueError):validated.materialize(self.root)


if __name__=='__main__':unittest.main()

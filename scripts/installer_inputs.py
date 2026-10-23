"""Validate explicit installer inputs without executing them or installing tools."""
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import zipfile

HOST = 'src/LayerHost/bin/Runtime/net10.0-windows/'
REQUIRED = {'viavw.exe', 'viavw.dll', 'viavw.runtimeconfig.json', 'coreclr.dll',
    HOST+'LayerHost.exe', HOST+'LayerHost.dll', HOST+'LayerHost.runtimeconfig.json',
    HOST+'VIAVW.Manager.exe', HOST+'VIAVW.Manager.dll', HOST+'VIAVW.Manager.runtimeconfig.json',
    'licenses/dotnet-LICENSE.TXT', 'licenses/dotnet-THIRD-PARTY-NOTICES.TXT',
    'licenses/windowsdesktop-LICENSE.TXT'}
MAX_FILES = 2000
MAX_BYTES = 600 * 1024 * 1024


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def member(name):
    path = PurePosixPath(name)
    if (not name or '\\' in name or path.is_absolute() or ':' in name or
        any(part in ('.','..') for part in name.split('/')) or str(path) != name):
        raise ValueError('Unsafe/noncanonical package member: '+name)
    reserved={'CON','PRN','AUX','NUL'}|{f'COM{i}' for i in range(1,10)}|{f'LPT{i}' for i in range(1,10)}
    if any(part.rstrip('. ')!=part or part.split('.')[0].upper() in reserved or
           any(ord(character)<32 or character in '<>"|?*' for character in part) for part in path.parts):
        raise ValueError('Unsafe Windows package filename: '+name)
    return name


def metadata(raw, expected_version=None, require_manager=True):
    if len(raw) > 1024 * 1024:
        raise ValueError('Package manifest is too large')
    data = json.loads(raw.decode('utf-8-sig'))
    if not isinstance(data,dict):raise ValueError('Expected object package manifest')
    version = data.get('version', '')
    if not isinstance(version,str) or not re.fullmatch(r'(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)', version):
        raise ValueError('MSI requires a numeric major.minor.patch package version')
    if any(value > limit for value,limit in zip(map(int,version.split('.')),(255,255,65535))):
        raise ValueError('Version exceeds MSI numeric limits')
    if expected_version is not None and version != expected_version:
        raise ValueError('Requested MSI version differs from package manifest')
    if not re.fullmatch(r'tool-[a-z0-9-]{1,80}',data.get('release','')):
        raise ValueError('Invalid package release identity')
    if data.get('schema') != 1 or data.get('platform') != 'win-x64' or data.get('selfContained') is not True:
        raise ValueError('Expected schema-1 self-contained Windows x64 package')
    files = data.get('files')
    if not isinstance(files,dict) or not 1 <= len(files) <= MAX_FILES:
        raise ValueError('Invalid package file manifest')
    folded = set()
    for name,value in files.items():
        member(name)
        if name.casefold() in folded or name.casefold() == 'package.json':
            raise ValueError('Duplicate/reserved package member')
        folded.add(name.casefold())
        if not isinstance(value,str) or not re.fullmatch(r'[0-9A-Fa-f]{64}',value):
            raise ValueError('Invalid SHA-256 in package manifest')
    required = REQUIRED if require_manager else {name for name in REQUIRED if 'VIAVW.Manager' not in name}
    if not required.issubset(files):
        raise ValueError('Package lacks required host/tool/manager/runtime/notice files')
    if not isinstance(data.get('hostAssemblySha256'),str) or data['hostAssemblySha256'].upper() != files[HOST+'LayerHost.dll'].upper():
        raise ValueError('Host assembly metadata disagrees with file manifest')
    return data


@dataclass
class Input:
    path: Path
    archive: bool
    manifest: dict
    manifest_bytes: bytes
    source_sha256: str

    def materialize(self, destination):
        destination=Path(destination)
        if destination.exists():
            raise ValueError('Payload destination already exists')
        destination.mkdir()
        expected=dict(self.manifest['files'])
        expected['package.json']=hashlib.sha256(self.manifest_bytes).hexdigest().upper()
        if self.archive:
            if digest(self.path) != self.source_sha256:
                raise ValueError('Archive changed after validation')
            with zipfile.ZipFile(self.path) as archive:
                for name,value in expected.items():
                    target=destination/member(name);target.parent.mkdir(parents=True,exist_ok=True)
                    with archive.open(name) as source,target.open('xb') as output:
                        shutil.copyfileobj(source,output,1024*1024)
                    if digest(target) != value.upper():
                        raise ValueError('Payload copy integrity failed: '+name)
        else:
            for name,value in expected.items():
                source=(self.path/member(name)).resolve()
                if not source.is_relative_to(self.path):
                    raise ValueError('Package member escapes package directory')
                target=destination/name;target.parent.mkdir(parents=True,exist_ok=True)
                shutil.copyfile(source,target)
                if digest(target) != value.upper():
                    raise ValueError('Package changed during payload copy: '+name)


def validate_archive(path, expected_sha256, version=None, require_manager=True):
    path=Path(path).resolve()
    if not path.is_file() or not expected_sha256 or digest(path) != expected_sha256.upper():
        raise ValueError('Archive path/hash mismatch; explicit archive SHA-256 is required')
    with zipfile.ZipFile(path) as archive:
        entries=archive.infolist()
        if len(entries)>MAX_FILES+1 or sum(entry.file_size for entry in entries)>MAX_BYTES:
            raise ValueError('Archive exceeds installer input budget')
        names=set();folded=set()
        for entry in entries:
            member(entry.filename)
            if entry.is_dir() or stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError('Only regular package files are permitted')
            if entry.filename.casefold() in folded:
                raise ValueError('Duplicate archive path')
            names.add(entry.filename);folded.add(entry.filename.casefold())
        if archive.getinfo('package.json').file_size>1024*1024:raise ValueError('Package manifest is too large')
        raw=archive.read('package.json')
        data=metadata(raw,version,require_manager)
        if names != set(data['files'])|{'package.json'}:
            raise ValueError('Archive contains missing/unmanifested files')
        for name,value in data['files'].items():
            with archive.open(name) as stream:
                if hashlib.file_digest(stream,'sha256').hexdigest().upper()!=value.upper():
                    raise ValueError('Archive file hash mismatch: '+name)
    return Input(path,True,data,raw,expected_sha256.upper())


def validate_package(path, manifest_sha256, version=None):
    path=Path(path).resolve();manifest=path/'package.json'
    if not manifest.is_file() or not manifest_sha256 or digest(manifest)!=manifest_sha256.upper():
        raise ValueError('Package manifest path/hash mismatch; explicit manifest SHA-256 is required')
    raw=manifest.read_bytes();data=metadata(raw,version)
    actual=set();total=0
    for file in path.rglob('*'):
        if file.is_symlink():
            raise ValueError('Symlink package entries are not permitted')
        if file.is_file():
            actual.add(file.relative_to(path).as_posix());total+=file.stat().st_size
    if total>MAX_BYTES or actual!=set(data['files'])|{'package.json'}:
        raise ValueError('Package directory contains missing/extra files or exceeds budget; use an untouched package')
    for name,value in data['files'].items():
        if digest(path/member(name))!=value.upper():
            raise ValueError('Package file hash mismatch: '+name)
    return Input(path,False,data,raw,manifest_sha256.upper())


def existing_tool(path, description):
    path=Path(path).resolve()
    if not path.is_file():
        raise FileNotFoundError(description+' is missing; supply an existing tool path. No installation/download is attempted: '+str(path))
    return path

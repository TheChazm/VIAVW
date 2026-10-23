"""Explicit installed-app/test dependency paths; never install or launch anything."""
import os
from pathlib import Path
import site


def application(variable, relative):
    override = os.environ.get(variable)
    path = Path(override) if override else Path(os.environ.get('ProgramFiles', r'C:\Program Files')) / relative
    if not path.is_absolute() or not path.is_file():
        raise FileNotFoundError(f'{variable}: set an absolute existing installed application path (default: {path}). No application was launched.')
    if path.name.casefold() != Path(relative).name.casefold():
        raise ValueError(f'{variable}: expected the installed {Path(relative).name} executable.')
    return str(path.resolve())


def add_sdk_path(root):
    override = os.environ.get('VIAVW_TEST_SDK')
    path = Path(override) if override else Path(root) / 'artifacts/tool-mcp-sdk-20261023'
    if override and (not path.is_absolute() or not path.is_dir()):
        raise FileNotFoundError('VIAVW_TEST_SDK must be an absolute existing dependency directory; no installation is attempted.')
    if path.is_dir():
        site.addsitedir(str(path))
    # Otherwise imports use the caller's existing environment/venv.

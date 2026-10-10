"""Paths, SteamVR settings access and process helpers shared by the watcher and the installer."""
import json
import os
import winreg

import psutil

APP_NAME = 'SteamVR Charge Guard'
APP_ID = 'SteamVRChargeGuard'
EXE_NAME = APP_ID + '.exe'
VERSION = '1.0.0'

INSTALL_DIR = os.path.join(os.environ['LOCALAPPDATA'], APP_ID)
INSTALLED_EXE = os.path.join(INSTALL_DIR, EXE_NAME)
STATE_FILE = os.path.join(INSTALL_DIR, 'install.json')
LOG_FILE = os.path.join(INSTALL_DIR, 'launcher.log')

RUN_KEY = r'Software\Microsoft\Windows\CurrentVersion\Run'
UNINSTALL_KEY = 'Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\' + APP_ID

STEAMVR_PROCESSES = {'vrserver.exe', 'vrmonitor.exe', 'vrstartup.exe'}
DEFAULT_STEAM_DIR = r'C:\Program Files (x86)\Steam'
AUTOLAUNCH_SECTION = 'power'
AUTOLAUNCH_KEY = 'autoLaunchSteamVROnButtonPress'


def _openvr_paths():
    try:
        path = os.path.join(os.environ['LOCALAPPDATA'], 'openvr', 'openvrpaths.vrpath')
        with open(path, encoding='utf-8-sig') as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def _steam_dir():
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Valve\Steam') as key:
            return os.path.normpath(winreg.QueryValueEx(key, 'SteamPath')[0])
    except OSError:
        return DEFAULT_STEAM_DIR


def steamvr_runtime():
    """SteamVR install folder, or None if SteamVR is not installed."""
    candidates = list(_openvr_paths().get('runtime', []))
    candidates.append(os.path.join(_steam_dir(), 'steamapps', 'common', 'SteamVR'))
    for runtime in candidates:
        if os.path.exists(os.path.join(runtime, 'bin', 'win64', 'vrstartup.exe')):
            return runtime
    return None


def steamvr_settings_path():
    for config in _openvr_paths().get('config', []):
        path = os.path.join(config, 'steamvr.vrsettings')
        if os.path.exists(path):
            return path
    return os.path.join(_steam_dir(), 'config', 'steamvr.vrsettings')


def steamvr_running():
    for p in psutil.process_iter(['name']):
        name = p.info['name']
        if name and name.lower() in STEAMVR_PROCESSES:
            return True
    return False


def _load_settings(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)


def read_autolaunch():
    """Steam's own auto-launch setting: True, False, or None when unset (SteamVR default = on)."""
    path = steamvr_settings_path()
    if not os.path.exists(path):
        return None
    try:
        value = _load_settings(path).get(AUTOLAUNCH_SECTION, {}).get(AUTOLAUNCH_KEY)
    except (OSError, ValueError):
        return None
    return value if isinstance(value, bool) else None


def write_autolaunch(value):
    """Set Steam's auto-launch setting (None removes it). SteamVR must not be running."""
    path = steamvr_settings_path()
    settings = _load_settings(path) if os.path.exists(path) else {}
    backup = path + '.chargeguard-backup'
    if os.path.exists(path) and not os.path.exists(backup):
        with open(path, 'rb') as src, open(backup, 'wb') as dst:
            dst.write(src.read())
    section = settings.setdefault(AUTOLAUNCH_SECTION, {})
    if value is None:
        section.pop(AUTOLAUNCH_KEY, None)
        if not section:
            settings.pop(AUTOLAUNCH_SECTION)
    else:
        section[AUTOLAUNCH_KEY] = value
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + '.tmp'
    # Same layout SteamVR writes: 3-space indent, "key" : value, CRLF.
    with open(tmp, 'w', encoding='utf-8', newline='\r\n') as f:
        json.dump(settings, f, indent=3, separators=(',', ' : '), ensure_ascii=False)
        f.write('\n')
    os.replace(tmp, path)


def load_state():
    try:
        with open(STATE_FILE, encoding='utf-8') as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def save_state(state):
    os.makedirs(INSTALL_DIR, exist_ok=True)
    with open(STATE_FILE, 'w', encoding='utf-8') as f:
        json.dump(state, f, indent=2)


def watcher_processes():
    """Running background watchers (both PyInstaller processes of each)."""
    me = psutil.Process()
    mine = {me.pid}
    try:
        mine.add(me.ppid())
    except psutil.Error:
        pass
    found = []
    for p in psutil.process_iter(['pid', 'name', 'cmdline']):
        try:
            if p.info['pid'] in mine or (p.info['name'] or '').lower() != EXE_NAME.lower():
                continue
            if '--run' in (p.info['cmdline'] or []):
                found.append(p)
        except psutil.Error:
            pass
    return found

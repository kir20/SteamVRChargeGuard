"""Install / uninstall: copy the exe, turn off Steam's own auto-launch, register the watcher at logon."""
import os
import shutil
import subprocess
import sys
import time
import winreg

import psutil

import common


class InstallError(Exception):
    """Raised with a translation key for the message to show."""


def is_installed():
    return os.path.exists(common.INSTALLED_EXE) and _run_entry() is not None


def _run_entry():
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, common.RUN_KEY) as key:
            return winreg.QueryValueEx(key, common.APP_ID)[0]
    except OSError:
        return None


def _set_run_entry(command):
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, common.RUN_KEY) as key:
        if command is None:
            try:
                winreg.DeleteValue(key, common.APP_ID)
            except FileNotFoundError:
                pass
        else:
            winreg.SetValueEx(key, common.APP_ID, 0, winreg.REG_SZ, command)


def _set_uninstall_entry(present):
    if not present:
        try:
            winreg.DeleteKey(winreg.HKEY_CURRENT_USER, common.UNINSTALL_KEY)
        except FileNotFoundError:
            pass
        return
    exe = common.INSTALLED_EXE
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, common.UNINSTALL_KEY) as key:
        for name, value in (('DisplayName', common.APP_NAME),
                            ('DisplayVersion', common.VERSION),
                            ('Publisher', common.APP_NAME),
                            ('DisplayIcon', exe),
                            ('InstallLocation', common.INSTALL_DIR),
                            ('UninstallString', f'"{exe}" --uninstall')):
            winreg.SetValueEx(key, name, 0, winreg.REG_SZ, value)
        winreg.SetValueEx(key, 'NoModify', 0, winreg.REG_DWORD, 1)
        winreg.SetValueEx(key, 'NoRepair', 0, winreg.REG_DWORD, 1)
        size_kb = os.path.getsize(exe) // 1024 if os.path.exists(exe) else 0
        winreg.SetValueEx(key, 'EstimatedSize', 0, winreg.REG_DWORD, size_kb)


def watcher_running():
    return bool(common.watcher_processes())


def stop_watchers():
    procs = common.watcher_processes()
    for p in procs:
        try:
            p.terminate()
        except psutil.Error:
            pass
    psutil.wait_procs(procs, timeout=5)


def start_watcher():
    subprocess.Popen([common.INSTALLED_EXE, '--run'], cwd=common.INSTALL_DIR, close_fds=True,
                     creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP)


def _this_exe():
    return sys.executable if getattr(sys, 'frozen', False) else None


def install():
    if common.steamvr_runtime() is None:
        raise InstallError('err_no_steamvr')
    if common.steamvr_running():
        raise InstallError('err_close_steamvr')
    src = _this_exe()
    if src is None:
        raise InstallError('err_not_frozen')

    stop_watchers()
    os.makedirs(common.INSTALL_DIR, exist_ok=True)
    if os.path.normcase(os.path.abspath(src)) != os.path.normcase(common.INSTALLED_EXE):
        for _ in range(10):  # the old watcher may need a moment to release the file
            try:
                shutil.copy2(src, common.INSTALLED_EXE)
                break
            except PermissionError:
                time.sleep(0.5)
        else:
            raise InstallError('err_copy')

    state = common.load_state() or {}
    if 'original_autolaunch' not in state:
        state['original_autolaunch'] = common.read_autolaunch()
    state['version'] = common.VERSION
    common.save_state(state)
    try:
        common.write_autolaunch(False)
    except (OSError, ValueError):
        raise InstallError('err_settings')

    _set_run_entry(f'"{common.INSTALLED_EXE}" --run')
    _set_uninstall_entry(True)
    start_watcher()


def uninstall():
    if common.steamvr_running():
        raise InstallError('err_close_steamvr')
    stop_watchers()
    _set_run_entry(None)
    _set_uninstall_entry(False)

    state = common.load_state() or {}
    original = state.get('original_autolaunch')
    try:
        common.write_autolaunch(original)
    except (OSError, ValueError):
        raise InstallError('err_settings')

    src = _this_exe()
    if src and os.path.normcase(os.path.dirname(os.path.abspath(src))) == os.path.normcase(common.INSTALL_DIR):
        # Can't delete the folder we're running from; let cmd do it after we exit.
        subprocess.Popen(f'cmd /c ping 127.0.0.1 -n 4 >nul & rmdir /s /q "{common.INSTALL_DIR}"',
                         creationflags=subprocess.CREATE_NO_WINDOW | subprocess.DETACHED_PROCESS, close_fds=True)
    else:
        shutil.rmtree(common.INSTALL_DIR, ignore_errors=True)

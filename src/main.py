"""SteamVR Charge Guard entry point.

  (no arguments)        install / uninstall window
  --run                 background watcher (what the logon entry starts)
  --run --dry-run       watcher that only logs what it would do
  --uninstall           uninstall with a confirmation prompt (Apps & features)
  --install --quiet     install without any window; exit code 0 = ok
  --uninstall --quiet   uninstall without any window
  --lang ko|ja|en       force the UI language
"""
import sys

import strings


def main(argv):
    if '--lang' in argv:
        i = argv.index('--lang')
        if i + 1 < len(argv) and argv[i + 1] in strings.TEXT:
            strings.LANG = argv[i + 1]

    if '--run' in argv:
        import watcher
        watcher.run(dry_run='--dry-run' in argv)
        return 0

    if '--quiet' in argv:
        import installer
        try:
            if '--install' in argv:
                installer.install()
            elif '--uninstall' in argv:
                installer.uninstall()
        except installer.InstallError as e:
            return {'err_close_steamvr': 3, 'err_no_steamvr': 4}.get(str(e), 2)
        return 0

    import ui
    if '--uninstall' in argv:
        ui.uninstall_from_settings()
    else:
        ui.Window().run()
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))

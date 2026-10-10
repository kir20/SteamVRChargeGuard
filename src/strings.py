"""UI text in Korean, Japanese and English, picked from the Windows display language."""
import ctypes

TEXT = {
    'ko': {
        'desc': ('충전 케이블을 꽂아서 컨트롤러·트래커가 켜졌을 때는 SteamVR이 자동으로 켜지지 않게 하고, '
                 '전원 버튼으로 켜거나 충전 케이블을 뽑았을 때만 SteamVR을 자동으로 켭니다.\n\n'
                 '설치하면 Steam 자체의 자동 실행 기능을 끄고, 이 프로그램이 Windows 로그인 때마다 '
                 '백그라운드에서 실행되어 대신 판단합니다.'),
        'program': '프로그램',
        'installed_running': '설치됨 · 실행 중',
        'installed_stopped': '설치됨 · 멈춤',
        'not_installed': '설치 안 됨',
        'steam_autolaunch': 'Steam 자체 자동 실행',
        'on': '켜짐',
        'off': '꺼짐',
        'steamvr': 'SteamVR',
        'running': '실행 중',
        'not_running': '꺼져 있음',
        'install': '설치',
        'reinstall': '다시 설치 / 업데이트',
        'uninstall': '제거',
        'open_log': '로그 보기',
        'install_done': ('설치가 끝났습니다.\n\nSteam을 한 번 재시작해야 Steam 자체의 자동 실행이 완전히 꺼집니다. '
                         '(또는 SteamVR을 한 번 켰다가 끄면 됩니다.)'),
        'uninstall_confirm': '제거하면 Steam 자체의 자동 실행 설정을 설치 전 상태로 되돌립니다. 제거할까요?',
        'uninstall_done': '제거했습니다. Steam을 재시작하면 원래 동작으로 돌아갑니다.',
        'err_no_steamvr': 'SteamVR이 설치되어 있지 않습니다.',
        'err_close_steamvr': 'SteamVR이 실행 중입니다. SteamVR을 완전히 종료한 뒤 다시 시도해 주세요.',
        'err_not_frozen': '실행 파일(.exe)로 실행해야 설치할 수 있습니다.',
        'err_copy': '프로그램 파일을 복사하지 못했습니다. 잠시 후 다시 시도해 주세요.',
        'err_settings': 'SteamVR 설정 파일(steamvr.vrsettings)을 바꾸지 못했습니다.',
        'error': '오류',
    },
    'ja': {
        'desc': ('充電ケーブルを挿してコントローラー・トラッカーの電源が入ったときは SteamVR を自動で起動せず、'
                 '電源ボタンで起動したときや充電ケーブルを抜いたときだけ SteamVR を自動で起動します。\n\n'
                 'インストールすると Steam 自体の自動起動機能をオフにし、このプログラムが Windows '
                 'ログイン時にバックグラウンドで動作して代わりに判断します。'),
        'program': 'プログラム',
        'installed_running': 'インストール済み · 動作中',
        'installed_stopped': 'インストール済み · 停止中',
        'not_installed': '未インストール',
        'steam_autolaunch': 'Steam 自体の自動起動',
        'on': 'オン',
        'off': 'オフ',
        'steamvr': 'SteamVR',
        'running': '起動中',
        'not_running': '停止中',
        'install': 'インストール',
        'reinstall': '再インストール / 更新',
        'uninstall': 'アンインストール',
        'open_log': 'ログを開く',
        'install_done': ('インストールが完了しました。\n\nSteam 自体の自動起動を完全にオフにするには Steam を一度再起動してください。'
                         '（SteamVR を一度起動して終了しても反映されます。）'),
        'uninstall_confirm': 'アンインストールすると Steam 自体の自動起動設定をインストール前の状態に戻します。よろしいですか？',
        'uninstall_done': 'アンインストールしました。Steam を再起動すると元の動作に戻ります。',
        'err_no_steamvr': 'SteamVR がインストールされていません。',
        'err_close_steamvr': 'SteamVR が起動しています。SteamVR を完全に終了してからもう一度お試しください。',
        'err_not_frozen': '実行ファイル (.exe) から起動したときのみインストールできます。',
        'err_copy': 'プログラムファイルをコピーできませんでした。しばらくしてからもう一度お試しください。',
        'err_settings': 'SteamVR の設定ファイル (steamvr.vrsettings) を変更できませんでした。',
        'error': 'エラー',
    },
    'en': {
        'desc': ('Stops SteamVR from starting when a controller or tracker switches on because you plugged it in to '
                 'charge, and starts SteamVR only when you turn a device on with its power button or unplug it '
                 'from the charger.\n\n'
                 "Installing turns off Steam's own auto-launch and runs this program in the background at every "
                 'Windows sign-in to make that decision instead.'),
        'program': 'Program',
        'installed_running': 'Installed · running',
        'installed_stopped': 'Installed · stopped',
        'not_installed': 'Not installed',
        'steam_autolaunch': "Steam's own auto-launch",
        'on': 'On',
        'off': 'Off',
        'steamvr': 'SteamVR',
        'running': 'Running',
        'not_running': 'Not running',
        'install': 'Install',
        'reinstall': 'Reinstall / update',
        'uninstall': 'Uninstall',
        'open_log': 'Open log',
        'install_done': ("Installed.\n\nRestart Steam once so Steam's own auto-launch is fully off. "
                         '(Starting and closing SteamVR once also works.)'),
        'uninstall_confirm': "Uninstalling restores Steam's own auto-launch setting to what it was before. Uninstall?",
        'uninstall_done': 'Uninstalled. Restart Steam to get the original behavior back.',
        'err_no_steamvr': 'SteamVR is not installed.',
        'err_close_steamvr': 'SteamVR is running. Close SteamVR completely and try again.',
        'err_not_frozen': 'Installing only works from the .exe build.',
        'err_copy': 'Could not copy the program file. Try again in a moment.',
        'err_settings': 'Could not change the SteamVR settings file (steamvr.vrsettings).',
        'error': 'Error',
    },
}


def language():
    primary = ctypes.windll.kernel32.GetUserDefaultUILanguage() & 0x3FF
    return {0x12: 'ko', 0x11: 'ja'}.get(primary, 'en')


LANG = language()


def t(key):
    return TEXT[LANG].get(key) or TEXT['en'][key]

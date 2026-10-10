# SteamVR Charge Guard

[한국어](#한국어) · [English](#english) · [日本語](#日本語)

**⬇ [Download SteamVRChargeGuard.exe](https://github.com/kir20/SteamVRChargeGuard/releases/latest)** (Windows 10/11, SteamVR)

> 🤖 이 프로젝트의 코드와 문서는 Claude(Claude Code)로 작성했습니다. / The code and documentation of this project were written with Claude (Claude Code). / このプロジェクトのコードとドキュメントは Claude（Claude Code）で作成しました。

## 한국어

컨트롤러나 트래커를 **충전하려고 케이블을 꽂았을 뿐인데 SteamVR이 켜지는** 문제를 막아 줍니다.
기기를 **전원 버튼으로 켜거나 충전 케이블을 뽑았을 때**는 SteamVR이 지금처럼 자동으로 켜집니다.

### 왜 생기는 문제인가요?
Steam은 SteamVR이 꺼져 있어도 무선 동글을 지켜보다가, 기기가 연결되면 SteamVR을 자동으로 켭니다.
그런데 GripVR 같은 일부 기기는 충전 케이블을 꽂으면 저절로 전원이 켜지기 때문에, 충전만 하려 해도 SteamVR이 켜집니다.

### 어떻게 해결하나요?
1. Steam 자체의 자동 실행을 끕니다 (`steamvr.vrsettings`의 `power.autoLaunchSteamVROnButtonPress = false`).
2. 이 프로그램이 Windows 로그인 때마다 백그라운드에서 실행되어 동글 신호를 직접 읽습니다.
   기기는 켜진 뒤 몇 초 안에 배터리 상태를 보내는데, 여기에 **충전 중인지 아닌지**가 들어 있습니다.
3. Steam 자동 실행을 끄면, 재부팅 뒤 Steam이 동글 라디오를 깨우던 일도 함께 멈춥니다.
   그래서 SteamVR이 꺼져 있는 동안에는 이 프로그램이 같은 일을 대신합니다. 설치된 SteamVR의 lighthouse 드라이버에 들어 있는 watchdog 모듈(Steam이 쓰는 것과 같은 것)을 띄워 동글이 기기 연결을 받게 합니다.
   SteamVR이 켜지면 그 모듈은 바로 내려갑니다.

| 상황 | SteamVR |
|---|---|
| 전원 버튼으로 켬 (충전 케이블 없음) | 자동으로 켜짐 |
| 충전 케이블을 꽂아서 저절로 켜짐 | 켜지지 않음 |
| 충전기에서 켜져 있던 기기의 케이블을 뽑음 | 자동으로 켜짐 |
| 신호가 약해서 잠깐 끊겼다 다시 연결됨 | 무시 |

### 설치
1. [Releases](https://github.com/kir20/SteamVRChargeGuard/releases/latest)에서 `SteamVRChargeGuard.exe`를 받아 실행합니다.
   "Windows의 PC 보호" 창이 뜨면 **추가 정보 → 실행**을 누르세요. 코드 서명이 없는 프로그램이라 나오는 경고입니다.
2. SteamVR을 끈 상태에서 **설치**를 누릅니다.
3. **Steam을 한 번 재시작**하면 적용됩니다.

### 제거
`SteamVRChargeGuard.exe`를 다시 실행해 **제거**를 누르거나, Windows **설정 → 앱**에서 제거합니다.
Steam 자동 실행 설정도 설치 전 상태로 되돌아갑니다.

### 알아두기
- USB 무선 동글(Watchman Dongle, Valve VR Radio)에 연결된 컨트롤러·트래커가 대상입니다.
- 헤드셋 버튼을 눌러 SteamVR을 켜는 기능(Vive 등)은 더 이상 동작하지 않습니다.
- 판단 기록은 `%LOCALAPPDATA%\SteamVRChargeGuard\launcher.log`에 남습니다. 창의 **로그 보기**로 열 수 있습니다.
- 1.1.0 이하에서는 PC를 재부팅하면 SteamVR을 한 번 켜기 전까지 기기가 동글에 연결되지 않아 자동 실행이 되지 않았습니다. 1.2.0에서 고쳤습니다.
- 확인한 기기: Shiftall GripVR VC2, VIVE Tracker 3.0 (Bigscreen Beyond 환경).

## English

Stops SteamVR from starting when a controller or tracker **switches itself on because you plugged it in to charge**.
SteamVR still starts automatically when you **turn a device on with its power button or unplug it from the charger**.

**Why it happens:** Steam watches the wireless dongles even while SteamVR is closed and starts SteamVR as soon as a device connects. Some devices (GripVR, ...) power on by themselves when a charging cable is plugged in.

**How it works:** Steam's own auto-launch is turned off (`power.autoLaunchSteamVROnButtonPress = false`), and this program runs in the background at sign-in. It reads the dongles directly: a few seconds after powering on, a device reports its battery status, which says whether it is charging.
Turning Steam's auto-launch off also stops Steam from waking the dongle radios after a reboot. So while SteamVR is closed, this program does that part itself. It runs the watchdog module from your SteamVR install's lighthouse driver (the same one Steam uses) so devices can connect, and it stops the module as soon as SteamVR starts. Before 1.2.0, devices could not connect after a reboot until SteamVR had been started once.

**Install:** download `SteamVRChargeGuard.exe` from [Releases](https://github.com/kir20/SteamVRChargeGuard/releases/latest) and run it (SmartScreen: *More info → Run anyway*; the exe is not code-signed), close SteamVR, click **Install**, then restart Steam once.
**Uninstall:** run the exe again and click **Uninstall**, or use Windows *Settings → Apps*. Steam's auto-launch setting is restored.

Works with controllers and trackers on USB dongles (Watchman Dongle, Valve VR Radio). Waking SteamVR with a headset button no longer works. Log: `%LOCALAPPDATA%\SteamVRChargeGuard\launcher.log`.

## 日本語

充電ケーブルを挿しただけでコントローラーやトラッカーの電源が入り、**SteamVR が起動してしまう問題**を防ぎます。
**電源ボタンで起動したとき・充電ケーブルを抜いたとき**は、これまでどおり SteamVR が自動で起動します。

**インストール:** [Releases](https://github.com/kir20/SteamVRChargeGuard/releases/latest) から `SteamVRChargeGuard.exe` をダウンロードして実行し（SmartScreen が出たら「詳細情報 → 実行」）、SteamVR を終了した状態で **インストール** を押し、Steam を一度再起動してください。
**アンインストール:** exe をもう一度実行して **アンインストール**、または Windows の「設定 → アプリ」から。Steam の自動起動設定も元に戻ります。

USB ドングル（Watchman Dongle / Valve VR Radio）に接続されたコントローラー・トラッカーが対象です。ヘッドセットのボタンで SteamVR を起動する機能は使えなくなります。

Steam の自動起動を切ると、再起動後に Steam がドングルの無線を起こす処理も止まります。そのため SteamVR が終了している間は、このプログラムがその役目を代わりに担います。インストール済み SteamVR の lighthouse ドライバーに入っている watchdog モジュール（Steam が使うものと同じ）を動かしてデバイスが接続できるようにし、SteamVR が起動するとすぐに止めます。1.1.0 以前は、再起動後に一度 SteamVR を起動するまでデバイスが接続できませんでした（1.2.0 で修正）。

## Build

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1   # -> dist\SteamVRChargeGuard.exe
```

A plain C# / .NET Framework 4.x WinForms program (~80 KB, no dependencies; .NET Framework 4.7.2+ ships with Windows 10 1803+ and 11).
Building needs the C# compiler from Visual Studio or Visual Studio Build Tools (".NET desktop build tools").

Versions up to 1.0.0 were Python + PyInstaller. That build was flagged by several antivirus engines (e.g. Microsoft `Wacatac.B!ml`), which is a common false positive for PyInstaller one-file executables, so 1.1.0 was rewritten in C#.

| File | Role |
|---|---|
| `src/Program.cs` | entry point, command-line options |
| `src/Watcher.cs` | background watcher (dongle reading, charging detection, SteamVR launch) |
| `src/Hid.cs` | Windows HID API: find dongles, read input reports |
| `src/RadioWake.cs` | keeps the dongle radios awake while SteamVR is closed by hosting SteamVR's lighthouse watchdog module (`IVRWatchdogProvider`) in a child process |
| `src/Installer.cs` | install / uninstall (copy, registry Run + Apps entry, SteamVR setting) |
| `src/Common.cs` | paths, log, SteamVR location and `steamvr.vrsettings` access |
| `src/Json.cs` | order- and number-preserving JSON for SteamVR settings files |
| `src/MainForm.cs`, `src/Strings.cs` | install window, ko / ja / en text |
| `assets/make_icon.py` | draws `assets/icon.ico` (Pillow; only needed to change the icon) |

Command line: `--run` (watcher), `--run --dry-run`, `--radio-host` (started by the watcher), `--install --quiet`, `--uninstall --quiet`, `--lang ko|ja|en`.

## License

[MIT](LICENSE)

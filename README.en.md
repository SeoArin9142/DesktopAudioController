# DesktopAudioController

[한국어](README.md) | **English**

DesktopAudioController is a Windows desktop utility for people who switch between several output devices, or who need quick control over per-app volume and output devices.

It is built for a narrower purpose than the Windows volume mixer: the main window shows only the output devices you pick, and each device card lets you control the apps that are actually playing sound on that device. Saved **profiles** switch your output device, microphone, device volumes and per-app outputs in one click, from the main window, the tray, a global hotkey or a command (e.g. an Elgato Stream Deck button).

> **Note:** The app's UI is currently **Korean only**. This README gives the Korean label in parentheses, e.g. Settings (`설정`), so you can find each button.

- Latest version: `v0.19.1`
- Distribution: `win-x64` portable zip (self-contained single-file exe, no .NET install needed)
- Requirements: Windows 10/11 x64
- Releases: <https://github.com/SeoArin9142/DesktopAudioController/releases>

## Features

```text
- Show only the output devices you choose (the current Windows default device is always on top)
- Per-device master volume / mute / set as default device
- Per-app volume / mute / custom display name, remembered and re-applied automatically
- Change the output device of individual apps
- Profiles: default output + input (microphone) device, fallback device order,
  per-app output devices, device volume / mute
- Switch profiles from main-window buttons, the tray menu, global hotkeys, or a command line (Stream Deck)
- Move profiles to another PC: devices are re-matched by name when device IDs differ
- Tray: current profile, one-click profile list, current output device volume in 10% steps
- In-app update with sha256 verification
- Masked diagnostic package and GitHub issue draft helper
- Optional debug log
```

## Quick start

1. Download `DesktopAudioController-v0.19.1-win-x64.zip` from the [releases page](https://github.com/SeoArin9142/DesktopAudioController/releases).
2. Extract it anywhere, for example:

```text
D:\DesktopAudioController\
  DesktopAudioController.exe
  DesktopAudioController.Updater.exe
```

3. Run `DesktopAudioController.exe`.
4. On first run the Settings window (`설정`) opens. Select the output devices to show on the main window and save (`저장`).
5. Optionally enable run at Windows startup (`Windows 로그인 후 자동 실행`) and minimize to tray (`닫기 버튼을 누르면 트레이로 최소화`).

Settings and logs are stored in `%LocalAppData%\DesktopAudioController`, not in the app folder, so overwriting the app with a new version keeps your settings.

Each release also includes a `.sha256` file:

```bash
sha256sum -c DesktopAudioController-v0.19.1-win-x64.zip.sha256
```

## Main window

- **Profile bar** (`프로필`): your saved profiles as numbered buttons. Click one to apply it; the current profile is highlighted in blue. The numbers match the tray menu and `--profile-index`.
- **Device cards**: one card per selected device, with the current Windows default device always on top.
  - Master volume slider and mute (`음소거`)
  - Set as default device (`기본 장치로 설정`)
  - Per-app volume panel (`프로그램별 볼륨 열기`): volume, mute, rename (`이름 변경`) and change output device (`출력 변경`) for each app playing on that device

Per-app volume and mute are saved. When the same app appears again, or moves to another device after you switch the default device, the saved values are applied right away without opening the panel.

## Profiles

Create a profile in Settings > Manual profiles (`수동 프로필`) with **Create profile** (`프로필 만들기`). It captures the current device list, display options and per-app volume/mute/name values.

Select a profile to open its editor (`... 프로필을 적용할 때`). For each profile you can also set:

```text
- Default output device (기본 출력 장치): sets both the Windows "default device" and "default communications device".
  - Fallback 1 / 2 (없으면 대신 1 / 2): used in order when the device is off or unplugged; a notification tells you.
- Default input / microphone (기본 입력(마이크)): also sets both default and communications roles,
  so apps like Discord follow it.
  - Fallback (없으면 대신)
- Per-app output devices (앱별 출력 장치): by executable name, e.g. TslGame.exe, chrome.exe, Discord.exe.
  "Follow Windows default" (Windows 기본 장치 따름) clears the per-app setting.
- Device volume / mute (장치 볼륨·음소거): e.g. start earphones at 20%, mute the speakers.
- Hotkey (단축키): e.g. Ctrl+Alt+1.
```

Press **Save** (`저장`) in Settings to store profile edits.

Example setup with speakers, a USB DAC for earphones and a wireless headset:

```text
1. Speakers    default output = speakers
2. Earphones   default output = USB DAC (fallback: speakers), DAC volume 20%, speakers muted
3. Headset     default output = headset (fallback: DAC -> speakers), microphone = headset mic
```

Apps without a per-app setting follow the default output device, so "send everything to this device" only needs the default output device.

## Hotkeys

Enable **Use profile hotkeys** (`프로필 단축키 사용`) in Settings and type a hotkey such as `Ctrl+Alt+1` in each profile's hotkey field.

```text
- Use at least one of Ctrl, Alt, Shift, Win (F13-F24 may be used alone).
- Keys: A-Z, 0-9, F1-F24, Num0-Num9, arrows, Space, Insert, Delete, Home, End, PageUp, PageDown, Pause
- Duplicate hotkeys are reported when you save.
- If another app already uses the combination, you are told it could not be registered.
```

## Apply profiles from the command line (Stream Deck, etc.)

```text
DesktopAudioController.exe --profile "Earphones"      apply by name (case/space-insensitive; a unique partial name also works)
DesktopAudioController.exe --profile-index 2          apply by the number shown on the main window / tray
DesktopAudioController.exe --toggle-mute "Speakers"   toggle mute of the output device whose name matches
```

If the app is already running, the command is passed to it through a named pipe and no new window opens. If it is not running, it starts quietly in the tray and applies the command.

Elgato Stream Deck: use the **System > Open** action with, for example:

```text
"D:\DesktopAudioController\DesktopAudioController.exe" --profile "Headset"
```

Hotkeys are keyboard input, so they can be blocked while a game running as administrator is in the foreground. Commands are not keyboard input and are not affected by this.

Results are shown as tray notifications (can be turned off with `단축키·명령으로 바뀔 때 알림 표시`) and written to the log. Unknown or ambiguous profile names and fallback device use are always notified.

## Tray menu

```text
- Current default device and "current output device volume" (현재 출력 장치 볼륨) in 10% steps
- Current profile (also shown in the tray icon tooltip)
- Profile list with numbers and hotkeys, one click to apply
- Open window / Settings / Refresh / Log folder
- Quick default device switch and device mute
- Exit
```

## Moving profiles to another PC

Device IDs differ between PCs even for the same device. Profiles also store device names, so after **Export settings** (`설정 내보내기`) on one PC and **Import settings** (`설정 가져오기`) on another, devices whose IDs don't match are found again by name. Open the profile in Settings once and save to store the new PC's device IDs.

## Per-app output device

Changing an app's output device uses Windows' internal per-app audio policy. Most desktop apps move immediately, but some apps (games, browsers) need to restart playback or restart the app before they use the new device. Executables running as administrator may refuse the change.

## Updates

The app checks GitHub Releases and can download the new zip, verify its sha256 and replace itself through `DesktopAudioController.Updater.exe`. Pre-releases are included only if you enable them in Settings. You can also update manually by closing the app and overwriting the folder with the new zip contents.

### SmartScreen

The zip is not code-signed. Windows SmartScreen or an "Unknown publisher" warning may appear, especially for files downloaded with a browser.

## Files and logs

```text
%LocalAppData%\DesktopAudioController\settings.json
%LocalAppData%\DesktopAudioController\settings.json.bak
%LocalAppData%\DesktopAudioController\logs\DesktopAudioController-YYYYMMDD.log
```

Logs are kept for 7 days, up to 30 files and 100 MB in total. Turn on **Debug log** (`디버그 로그 기록`) only when investigating a problem.

**Export diagnostic package** (`진단 패키지 내보내기`) creates a zip that masks paths, device IDs, session IDs and profile IDs. App names, executable names and device names may remain, so check the contents before attaching it to a public issue.

## Known limitations

```text
- Windows only (x64). No ARM64 native build. No macOS / Linux.
- UI is Korean only.
- Per-app output changes depend on an internal Windows API; some apps need a restart.
- Per-app output in profiles matches by executable name, so apps with the same name are changed together.
- Not code-signed, so SmartScreen warnings may appear.
- Actual device/session behavior depends on audio drivers and app implementations.
```

## Building from source

Requires the .NET 8 SDK on Windows (or Windows targeting enabled).

```bash
dotnet build src/DesktopAudioController/DesktopAudioController.csproj -p:EnableWindowsTargeting=true
dotnet test tests/DesktopAudioController.Tests
bash scripts/publish-win-x64.sh
```

The publish script creates:

```text
artifacts/release/packages/DesktopAudioController-<version>-win-x64.zip
artifacts/release/packages/DesktopAudioController-<version>-win-x64.zip.sha256
```

Tech stack: C# / .NET 8 / WPF, NAudio for Windows Core Audio, a single exe that also runs short-lived worker processes for device enumeration and per-app output changes so that a stuck audio API call cannot freeze the main window.

## License

MIT License. See [LICENSE](LICENSE).

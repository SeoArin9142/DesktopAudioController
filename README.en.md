# DesktopAudioController

[한국어](README.md) | **English**

DesktopAudioController is a Windows desktop utility for people who switch between several output devices, or who need quick control over per-app volume and output devices.

It is built for a narrower purpose than the Windows volume mixer: the main window shows only the output devices you pick, and each device card lets you control the apps that are actually playing sound on that device. Saved **profiles** switch your output device, microphone, device volumes and per-app outputs in one click, from the main window, the tray, a global hotkey or a command (e.g. an Elgato Stream Deck button).

> **Language:** The UI is available in **English and Korean**. It follows the Windows display language by default; use the `English` / `한국어` button at the top of the main window to switch instantly (no restart). You can also choose Auto / Korean / English in Settings.

- Latest version: `v0.20.1`
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
- English / Korean UI, switchable from the main window without restarting
```

## Quick start

1. Download `DesktopAudioController-v0.20.1-win-x64.zip` from the [releases page](https://github.com/SeoArin9142/DesktopAudioController/releases).
2. Extract it anywhere, for example:

```text
D:\DesktopAudioController\
  DesktopAudioController.exe
  DesktopAudioController.Updater.exe
```

3. Run `DesktopAudioController.exe`.
4. On first run the Settings window opens. Select the output devices to show on the main window and save.
5. Optionally enable run at Windows startup and minimize to tray.

Settings and logs are stored in `%LocalAppData%\DesktopAudioController`, not in the app folder, so overwriting the app with a new version keeps your settings.

Each release also includes a `.sha256` file:

```bash
sha256sum -c DesktopAudioController-v0.20.1-win-x64.zip.sha256
```

## Main window

- **Profile bar**: your saved profiles as numbered buttons. Click one to apply it; the current profile is highlighted in blue. The numbers match the tray menu and `--profile-index`.
- **Device cards**: one card per selected device, with the current Windows default device always on top.
  Device names come from Windows (e.g. a Korean Windows shows `스피커(...)` for speakers), so they don't change with the app language. Rename a device in Windows Settings > System > Sound to change it.
  - Master volume slider and mute
  - Set as default device
  - Per-app volume panel: volume, mute, rename and change output device for each app playing on that device

Per-app volume and mute are saved. When the same app appears again, or moves to another device after you switch the default device, the saved values are applied right away without opening the panel.

## Profiles

Create a profile in Settings > Manual profiles with **Create profile**. It captures the current device list, display options and per-app volume/mute/name values.

Select a profile to open its editor. For each profile you can also set:

```text
- Default output device: sets both the Windows "default device" and "default communications device".
  - Fallback 1 / 2: used in order when the device is off or unplugged; a notification tells you.
- Default input / microphone: also sets both default and communications roles,
  so apps like Discord follow it.
  - Fallback
- Per-app output devices: by executable name, e.g. TslGame.exe, chrome.exe, Discord.exe.
  "Follow Windows default" clears the per-app setting.
- Device volume / mute: e.g. start earphones at 20%, mute the speakers.
- Hotkey: e.g. Ctrl+Alt+1.
```

Press **Save** in Settings to store profile edits.

Example setup with speakers, a USB DAC for earphones and a wireless headset:

```text
1. Speakers    default output = speakers
2. Earphones   default output = USB DAC (fallback: speakers), DAC volume 20%, speakers muted
3. Headset     default output = headset (fallback: DAC -> speakers), microphone = headset mic
```

Apps without a per-app setting follow the default output device, so "send everything to this device" only needs the default output device.

## Hotkeys

Enable **Use profile hotkeys** in Settings and type a hotkey such as `Ctrl+Alt+1` in each profile's hotkey field.

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

Results are shown as tray notifications (can be turned off with "Show a notification when switched by hotkey or command") and written to the log. Unknown or ambiguous profile names and fallback device use are always notified.

## Tray menu

```text
- Current default device and "current output device volume" in 10% steps
- Current profile (also shown in the tray icon tooltip)
- Profile list with numbers and hotkeys, one click to apply
- Open window / Settings / Refresh / Log folder
- Quick default device switch and device mute
- Exit
```

## Moving profiles to another PC

Device IDs differ between PCs even for the same device. Profiles also store device names, so after **Export settings** on one PC and **Import settings** on another, devices whose IDs don't match are found again by name. Open the profile in Settings once and save to store the new PC's device IDs.

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

Logs are kept for 7 days, up to 30 files and 100 MB in total. Turn on **Debug log** only when investigating a problem.

**Export diagnostic package** creates a zip that masks paths, device IDs, session IDs and profile IDs. App names, executable names and device names may remain, so check the contents before attaching it to a public issue.

## Known limitations

```text
- Windows only (x64). No ARM64 native build. No macOS / Linux.
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

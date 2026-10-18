# SekaiPad

**Use your Windows Precision Touchpad as a multi-touch controller for Android rhythm games.**

> **Experimental source alpha.** Rhythm mode temporarily controls Windows mouse and touchpad gestures. Learn F10 (restore) and F6 (exit) before playing.

SekaiPad converts multiple physical Precision Touchpad contacts into independent Android touch events. Each contact retains its own finger identity and mapped screen position for simultaneous holds, slides and flicks.

**First tested configuration:** Windows 11 25H2 (build 26200), MuMu Android 15 and Project SEKAI. SekaiPad is unofficial and is **not affiliated with SEGA, Colorful Palette or the Project SEKAI development team**. The source preview contains controller code and upstream notices; obtain the game and emulator separately and follow their terms.

## How it works

```text
Laptop Precision Touchpad (Raw HID, digitizer 0x0D / 0x05)
    -> native contact-frame assembly (stable finger IDs)
    -> absolute game-screen mapping (1920 x 1080)
    -> scrcpy control-only injection via local ADB reverse
    -> Android emulator touch events
```

Windows normally also interprets the touchpad as a mouse and system-gesture input device. In rhythm mode, SekaiPad suppresses regular mouse events, temporarily suspends three-/four-finger Windows gestures, and provides a click-through, lilac-colored finger-position overlay aligned to MuMu. Android also provides a native "Show taps" setting. The host overlay displays **mapped HID contacts**; Android "Show taps" displays touches received by the emulator. Check actual note judgments separately in game.

## Status

- ✅ Windows Raw Input Precision Touchpad decoding; independently tracked contacts.
- ✅ Absolute full-screen mapping and optional lane-focused mapping.
- ✅ Multi-finger touch serialization and control transport over local scrcpy.
- ✅ Host-side mapped-contact markers; native Android touch-dot option.
- ✅ F10 return to normal input and independent crash-recovery guard.
- ✅ Tester-reported multi-finger play on a Project SEKAI **APPEND** chart using a laptop touchpad.
- 🧪 **Ongoing:** note timing, sustained holds/slides/flicks and device compatibility. See [compatibility](docs/COMPATIBILITY.md).

## Shortcuts

| Key | Action |
| --- | --- |
| **F7** | Enter absolute full-screen rhythm mode |
| **F8** | Enter lane-focused rhythm mode |
| **F9** | Switch mapping |
| **F10** | Restore normal Windows mouse and gesture behavior |
| **F6** | Exit SekaiPad, releasing active touches |

`Esc` remains available to the emulator. Keep a physical keyboard accessible for F10/F6.

## Build from source

Requires Windows and the **.NET 10 SDK**. For the GUI at runtime, install the corresponding **.NET 10 Desktop Runtime**. The emulator and the official scrcpy server are external dependencies. See [building instructions](docs/BUILDING.md).

```powershell
dotnet restore src/SekaiPad/SekaiPad.csproj
dotnet build src/SekaiPad/SekaiPad.csproj -c Release --no-restore

dotnet restore src/SekaiPad.CursorGuard/SekaiPad.CursorGuard.csproj
dotnet build src/SekaiPad.CursorGuard/SekaiPad.CursorGuard.csproj -c Release --no-restore

dotnet restore tests/SekaiPad.ScrcpySmoke/SekaiPad.ScrcpySmoke.csproj
dotnet run --project tests/SekaiPad.ScrcpySmoke/SekaiPad.ScrcpySmoke.csproj -c Release --no-restore
```

Build an experimental Windows ZIP with `tools/package-alpha.ps1`. It requires the .NET 10 Desktop Runtime; set up MuMu, the game and the official scrcpy server separately. Use `tools/stage-scrcpy-server.ps1` to add the server to your extracted preview.

## Safety and privacy

Rhythm mode blocks **all Windows mice** and suspends three-/four-finger system gestures, while touchpad contacts control the local emulator. Press **F10** to restore desktop input or **F6** to exit. Gesture suppression is tested on Windows build 26200; crash restoration is still under real-device evaluation. See [safety details](docs/SAFETY.md).

For setup, see [building instructions](docs/BUILDING.md). Share only sanitized diagnostic screenshots and logs.

## License and credits

Original SekaiPad code is [MIT licensed](LICENSE). Upstream copyrights and license texts are listed in [third-party notices](THIRD_PARTY_NOTICES.md).

Thanks to [RawInput.Touchpad](https://github.com/emoacht/RawInput.Touchpad) and [scrcpy](https://github.com/Genymobile/scrcpy). The optional legacy Android Emulator protocol definitions originate from AOSP.

We welcome diagnostic reports and PRs; see [CONTRIBUTING](CONTRIBUTING.md).

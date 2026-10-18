# SekaiPad

**Use your Windows Precision Touchpad as a multi-touch controller for Android rhythm games.**

> **Experimental alpha / source preview.** Rhythm mode temporarily suppresses mouse input and changes Windows touchpad gestures. Test the emergency restore keys before playing.

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
- ✅ F10 restoration and crash-recovery design for desktop input / Windows gesture settings.
- ✅ Tester-reported multi-finger play on a Project SEKAI **APPEND** chart using a laptop touchpad.
- 🧪 **Planned observations during normal play:** note judgment timing and extended HOLD/SLIDE/FLICK reliability; cross-device support needs additional hardware reports. See [compatibility notes](docs/COMPATIBILITY.md).
- ⚠️ The Windows gesture suppression path uses a Windows 11-specific, undocumented shell interface. Compatibility is currently verified only for the tested Windows build.
- ⚠️ The mouse suppression path affects *all* mice in the Windows session while rhythm mode is active. Keyboard emergency keys remain active.

## Shortcuts

| Key | Action |
| --- | --- |
| **F7** | Enter absolute full-screen rhythm mode |
| **F8** | Enter lane-focused rhythm mode |
| **F9** | Switch mapping |
| **F10** | Restore normal Windows mouse and gesture behavior |
| **F6** | Exit SekaiPad, releasing active touches |

`Esc` is not captured by SekaiPad. **Confirm F10 and F6 work before using the touchpad for gameplay.**

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

No official installer or stable release is available yet. A **framework-dependent, source-built preview package** can be prepared with `tools/package-alpha.ps1` after the release checks pass. MuMu, the official scrcpy server and game installation are prepared separately; `tools/stage-scrcpy-server.ps1` can stage the scrcpy server into a local unpacked preview.

## Safety and privacy

SekaiPad sends game input locally from the desktop to the selected emulator. During rhythm mode it can modify four `HKCU` Precision Touchpad gesture settings and install a low-level Windows mouse hook. Its restoration handlers are designed for F10, F6, bridge failures and shutdown; an independent guard handles unexpected process exits. **Windows updates, other gesture utilities or unusual failures may still interfere.** Keep a physical keyboard available.

Read [safety details](docs/SAFETY.md) and [the compatibility/testing guide](docs/BUILDING.md) before use. Redact account details, authentication tokens and personal game data before sharing diagnostic screenshots.

## License and credits

Original SekaiPad code: **MIT**, see [LICENSE](LICENSE). Third-party components retain their respective licenses and notices; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and any pre-existing `THIRD_PARTY_LICENSES.md` in the source tree.

Thanks to [RawInput.Touchpad](https://github.com/emoacht/RawInput.Touchpad) and [scrcpy](https://github.com/Genymobile/scrcpy). The optional legacy Android Emulator protocol definitions originate from AOSP.

We welcome diagnostic reports and PRs; see [CONTRIBUTING](CONTRIBUTING.md).

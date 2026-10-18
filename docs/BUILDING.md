# Building SekaiPad from source

**Current reference environment:** Windows 11 25H2 build 26200, MuMu Android 15 and Project SEKAI. The .NET 10 source build and synthetic tests are verified; gameplay observations are collected separately. Gesture suppression relies on a Windows Shell cache interface tested on build 26200.

## Requirements

Windows Precision Touchpad and physical keyboard (F10/F6); .NET 10 SDK for builds; .NET 10 Desktop Runtime for framework-dependent binaries; MuMu Android 15 with local ADB at `127.0.0.1:5555`; official scrcpy v5.0 server from Genymobile. Obtain scrcpy from the official Genymobile release and keep game APKs external to SekaiPad distributions.

## Source build

```powershell
dotnet restore src/SekaiPad/SekaiPad.csproj
dotnet build src/SekaiPad/SekaiPad.csproj -c Release --no-restore
dotnet restore src/SekaiPad.CursorGuard/SekaiPad.CursorGuard.csproj
dotnet build src/SekaiPad.CursorGuard/SekaiPad.CursorGuard.csproj -c Release --no-restore
dotnet restore tests/SekaiPad.ScrcpySmoke/SekaiPad.ScrcpySmoke.csproj
dotnet run --project tests/SekaiPad.ScrcpySmoke/SekaiPad.ScrcpySmoke.csproj -c Release --no-restore
```

Run Windows UI/HID/input-suppression smoke tests only in an interactive desktop. They can affect the current Windows input session; **do not run them on hosted CI** or when a game is active.

## Portable preview

After a clean-source review and compilation, run:

```powershell
./tools/package-alpha.ps1 -Version '0.1.0-alpha'
```

The ZIP is **framework-dependent**, not an installer. It includes neither .NET Desktop Runtime nor scrcpy-server, MuMu binaries, Project SEKAI data, private SDK caches or credentials. GitHub Actions compiles and audits this ZIP on the runner, while uploading binaries only through a separate restricted, opt-in private workflow.

After obtaining scrcpy v5.0 from the **official** Genymobile release, stage the extracted `scrcpy-server` locally into an **unpacked preview directory**, using `tools/stage-scrcpy-server.ps1`. Supply the trusted expected SHA256 for the strongest source verification. The staging helper verifies copied bytes, preserves already staged files, and refuses source Git checkouts and directory junctions. It does not download or execute the server. Do not commit or redistribute the staged server with SekaiPad by default.

For a built preview, run `python tools/audit-alpha-zip.py --root . --zip dist/SekaiPad-0.1.0-alpha-win-x64.zip` to check the exact ZIP member set, runtime versions and upstream license text before sharing it.

## Diagnostics

Raw HID counts prove Windows decoded the pad; scrcpy packet counters prove local socket writes; the violet MuMu overlay displays mapped HID positions. Confirm actual Project SEKAI note judgments separately by testing hits, hold, slide, flick, chords and delay. F10 restores Windows input; F6 exits. Esc remains available to the emulator.

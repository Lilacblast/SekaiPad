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

Run hardware/input-isolation tests on an attended Windows desktop with a physical keyboard. Hosted CI runs synthetic tests.

## Portable preview

After a clean-source review and compilation, run:

```powershell
./tools/package-alpha.ps1 -Version '0.1.0-alpha'
```

The Windows ZIP requires the .NET 10 Desktop Runtime. Set up MuMu, the game and scrcpy separately. CI verifies the compiled ZIP and third-party notices.

Obtain scrcpy v5.0 from Genymobile and use `tools/stage-scrcpy-server.ps1` to copy its server into an extracted preview. Supply the official SHA256 where available; the helper checks file integrity and protects existing files and source directories.

For a built preview, run `python tools/audit-alpha-zip.py --root . --zip dist/SekaiPad-0.1.0-alpha-win-x64.zip` to check the exact ZIP member set, runtime versions and upstream license text before sharing it.

## Diagnostics

HID counters show decoded touchpad reports, scrcpy counters show local packets, and purple markers show mapped contacts. Confirm note hits and timing in the game. F10 restores Windows input; F6 exits. Esc remains with the emulator.

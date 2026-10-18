# Contributing to SekaiPad

Thank you for helping test and improve touchpad-to-Android multi-touch control.

## Good first contributions

- Device compatibility tests on Windows Precision Touchpads (manufacturer/model, Windows build, exact active touch count).
- Reproducible synthetic HID/codec tests; performance benchmarks that work **without** the game or emulator.
- Accessible diagnostics and clearer feedback when the bridge or input isolation fails.
- Documentation, translations and build reproducibility.

## Working on input handling

Changes to `WindowsGestureGuard`, `GestureSettings`, `LowLevelMouseGuard`, `SystemCursorVisibility`, `MainWindow` hotkeys or native P/Invoke code are **safety-sensitive**. Preserve all of these invariants:

1. Keep the keyboard and **F10 (restore)** / **F6 (exit)** available throughout rhythm mode.
2. On a hook/gesture setup failure, stay in normal Windows input mode.
3. Normal mode, disconnect, fault, closing, and unexpected exits must recover normal desktop input and release all held Android contacts.
4. Record original HKCU settings, including keys that were originally absent; restore *exactly* those states.
5. Guard undocumented Windows Shell/COM APIs with an explicitly verified build and version.
6. Activate Android game input only through an explicit user action after launch. Keep diagnostics free of generated game taps.
7. Commit source and sanitized examples. Keep emulator/game binaries, account data, tokens, private logs and disk images in local-only storage.

Run at least:

```powershell
dotnet restore tests/SekaiPad.ScrcpySmoke/SekaiPad.ScrcpySmoke.csproj
dotnet run --no-restore --project tests/SekaiPad.ScrcpySmoke/SekaiPad.ScrcpySmoke.csproj -c Release
```

Run attended Windows hardware tests between play sessions. Include Windows build, device, restore behavior and observed note accuracy with your results.

## Pull requests

Keep changes narrowly scoped, include a test where practical, describe expected failure and restoration behavior, and reference the Windows build and device used. A pull request modifying touch suppression must include a manual recovery walkthrough.

## Device/bug reports

Provide the SekaiPad commit, Windows version/build, touchpad brand/model, emulator/version, input mode, steps, expected/actual results and whether F10 restored Windows control. Share aggregated statistics or sanitized snippets; keep raw input captures, memory dumps and full screenshots private.

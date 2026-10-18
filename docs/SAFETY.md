# Important: native Windows input safety

SekaiPad is experimental. In F7/F8 rhythm mode it (1) installs a low-level mouse hook affecting **all** mice in the Windows session, (2) temporarily modifies four HKCU Windows Precision Touchpad three/four-finger tap/slide settings, and (3) optionally hides the system cursor images over MuMu. All changes should be restored on normal mode, disconnect, close or bridge fault; an independent guard attempts crash recovery.

Gesture suppression uses an undocumented Windows Shell cache interface, tested on Windows 11 25H2 build 26200. Other builds require compatibility testing. An input-hook or gesture-guard failure prevents rhythm mode.

**F10**: immediately restore Windows mouse and gestures while SekaiPad remains open.

**F6**: exit SekaiPad and release input contacts.

If recovery hotkeys fail, use a **physical keyboard** to close SekaiPad in Task Manager. Windows releases global hooks upon process termination; the crash guard attempts to restore cursor and gesture settings. If that fails, restarting the Windows user session may be necessary.

CI verifies the crash guard lifecycle, PID/start-time checks and acknowledgment cleanup using only a dummy child process and synthetic markers. Hosted tests leave global Windows mouse, cursor and gesture settings unchanged; real crash-recovery performance remains for an attended device check.

Crash-recovery markers remain available after a failed system cursor restoration. The independent helper retries up to three times and retains the marker for diagnosis when restoration fails.

Never run with Administrator privileges merely to bypass a safety failure, disable the Precision Touchpad device or attempt to block keyboard input. Do not upload accounts, raw HID captures, full logcat dumps, dumps or private screenshots to public issues.

SekaiPad sends input to the selected local emulator. Host contact circles show mapped HID positions, while gameplay judgments require separate confirmation in game. The Android **Show taps** setting is normally restored to its prior value; after an unexpected exit, check Android Developer options and restore that setting if needed.

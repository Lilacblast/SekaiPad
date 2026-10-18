# Important: native Windows input safety

SekaiPad is experimental. In F7/F8 rhythm mode it (1) installs a low-level mouse hook affecting **all** mice in the Windows session, (2) temporarily modifies four HKCU Windows Precision Touchpad three/four-finger tap/slide settings, and (3) optionally hides the system cursor images over MuMu. All changes should be restored on normal mode, disconnect, close or bridge fault; an independent guard attempts crash recovery.

Gesture suppression uses an undocumented Windows Shell cache interface, tested on Windows 11 25H2 build 26200. Other builds require compatibility testing. An input-hook or gesture-guard failure prevents rhythm mode.

**F10**: immediately restore Windows mouse and gestures while SekaiPad remains open.

**F6**: exit SekaiPad and release input contacts.

If recovery hotkeys fail, use a **physical keyboard** to close SekaiPad in Task Manager. Windows releases global hooks upon process termination; the crash guard attempts to restore cursor and gesture settings. If that fails, restarting the Windows user session may be necessary.

CI verifies crash-guard lifecycle and recovery policies with synthetic input. Real system-wide crash restoration remains to be tested on a supported Windows device.

Crash-recovery markers remain available after a failed system cursor restoration. The independent helper retries up to three times and retains the marker for diagnosis when restoration fails.

Run as a standard Windows user, with your physical keyboard available. Keep account data, raw HID captures, full logcat dumps and private screenshots out of public issues.

SekaiPad sends input to the selected local emulator. Host contact circles show mapped HID positions, while gameplay judgments require separate confirmation in game. The Android **Show taps** setting is normally restored to its prior value; after an unexpected exit, check Android Developer options and restore that setting if needed.

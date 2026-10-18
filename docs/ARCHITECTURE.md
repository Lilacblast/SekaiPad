# SekaiPad architecture

```
Windows Precision Touchpad Raw HID (usage 0x0D/0x05)
  -> Input/TouchpadHelper.cs (WM_INPUT)
  -> Input/TouchpadFrameAssembler.cs (complete parallel/hybrid frames)
  -> TouchMapping.cs (stable contact IDs / absolute coordinates)
      -> MuMuHostTouchOverlay.cs (optional Windows lilac dot overlay, no injection)
  -> ScrcpyTouchPackets.cs (32-byte scrcpy control packets)
  -> ScrcpyFramePump.cs (one TCP write per complete touch frame; safe UP on shutdown)
  -> ScrcpyTouchBridge.cs (bounded frames -> localhost TCP -> ADB reverse)
  -> MuMu Android 15 input dispatcher -> game
```

The optional legacy Google Android Emulator transport uses a different backend. Current MuMu support is tied to explicit loopback ADB serial `127.0.0.1:5555`, scrcpy v5.0, and a 1920×1080 landscape target. Compatibility with other game/emulator versions is unverified.

**Separate safety plane:** LowLevelMouseGuard suppresses Windows mouse events; WindowsGestureGuard/GestureSettings temporarily disable three/four-finger Windows gestures with backup; SystemCursorVisibility hides/restores cursor images; CursorGuard restores settings after unexpected process exit. F10 and F6 must remain usable.

Diagnostics track HID reports, scrcpy writes and in-game note judgments separately. Purple markers show mapped contact positions.

During disconnect or input-queue saturation, the frame pump discards unprocessed gameplay frames and sends UP for the contacts actually emitted to the local TCP stream. It measures enqueue-to-local-pump duration with a monotonic clock and exposes the last/peak observations in diagnostics. This host-side measurement includes queueing and encoding; Android delivery, rendering and game-note judgment need independent acceptance tests.

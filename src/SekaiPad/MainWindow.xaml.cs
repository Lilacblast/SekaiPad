using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Windows.Threading;
using RawInput.Touchpad;

namespace SekaiPad;

public partial class MainWindow : Window
{
    private TouchMapping _mapping = new();
    private readonly DispatcherTimer _watchdog;
    // Visual work runs below input priority: it must never block HID->Android sends.
    private readonly DispatcherTimer _visualTimer;
    private TouchpadContact[] _latestContacts = Array.Empty<TouchpadContact>();
    private bool _diagnosticsPending;
    private bool _overlayPending;
    private DateTime _lastOverlayRefreshUtc = DateTime.MinValue;
    private ITouchBridge? _bridge;
    private HwndSource? _source;
    private bool _enabled;
    private bool _connecting;
    private bool _isClosing;
    private bool _readyToClose;
    private DateTime _lastContactReportUtc = DateTime.MinValue;
    private DateTime _lastUiUpdateUtc = DateTime.MinValue;
    private bool _touchpadPresent;
    private bool _activatingRhythmMode;
    private readonly HashSet<int> _registeredHotkeys = new();
    private ICursorVisibility _cursorVisibility = new SystemCursorVisibility();
    private Func<bool> _cursorTarget = EmulatorPointerRegion.IsMuMuFocusedUnderPointer;
    private bool _cursorHideFailed;
    private IMouseInputGuard _mouseGuard = new LowLevelMouseGuard();
    private IWindowsGestureGuard _gestureGuard = new WindowsGestureGuard();
    private IEmulatorTouchMarkers _guestTouches = new MuMuTouchMarkers();
    private IHostTouchOverlay _hostOverlay = new MuMuHostTouchOverlay();

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HOTKEY_FULL = 0x5E71;
    private const int HOTKEY_LANES = 0x5E72;
    private const int HOTKEY_SWITCH = 0x5E73;
    private const int HOTKEY_NORMAL = 0x5E74;
    private const int HOTKEY_EXIT = 0x5E75;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            // Keep F6 available to other apps outside active rhythm mode.
            // Within the focused SekaiPad window, F6 still closes in normal mode.
            if (e.Key == Key.F6 && !_enabled)
            {
                e.Handled = true;
                Close();
            }
        };
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(65) };
        _watchdog.Tick += (_, _) =>
        {
            // A lost HID UP report must never leave a finger stuck forever.
            if (_enabled && _mapping.ActiveCount > 0 &&
                DateTime.UtcNow - _lastContactReportUtc > TimeSpan.FromSeconds(15))
                ReleaseAll("No PTP frame for 15s: emergency release of stale touches");
            // This high-priority safety tick never redraws WPF overlays.
            UpdateCursorVisibility();
        };
        _watchdog.Start();
        _visualTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _visualTimer.Tick += (_, _) => RefreshVisuals();
        _visualTimer.Start();
    }

    private void RefreshVisuals()
    {
        if (_diagnosticsPending &&
            DateTime.UtcNow - _lastUiUpdateUtc >= TimeSpan.FromMilliseconds(50))
        {
            _lastUiUpdateUtc = DateTime.UtcNow;
            _diagnosticsPending = false;
            DrawDiagnostics(_latestContacts);
        }

        if (!_hostOverlay.Active) return;
        DateTime now = DateTime.UtcNow;
        // Contact visuals: <=30 FPS. Idle focus/geometry: <=10 FPS.
        // Both are below Windows HID input priority on the dispatcher.
        if (!_overlayPending &&
            now - _lastOverlayRefreshUtc < TimeSpan.FromMilliseconds(100))
            return;
        bool renderContacts = _overlayPending;
        _overlayPending = false;
        try
        {
            if (renderContacts)
                _hostOverlay.Render(_latestContacts, _mapping,
                    TouchpadHelper.MinX, TouchpadHelper.MaxX,
                    TouchpadHelper.MinY, TouchpadHelper.MaxY);
            _hostOverlay.Refresh();
            _lastOverlayRefreshUtc = now;
        }
        catch (Exception ex)
        {
            try { _hostOverlay.Disable(); } catch { }
            StatusText.Text = "Touch overlay failed: " + ex.Message;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        _source?.AddHook(WndProc);
        _touchpadPresent = TouchpadHelper.Exists();
        bool registered = _touchpadPresent &&
                          TouchpadHelper.RegisterInput(_source?.Handle ?? IntPtr.Zero);
        StatusText.Text = registered
            ? "Precision Touchpad ready."
            : _touchpadPresent ? "Touchpad Raw Input registration failed."
            : "No Windows Precision Touchpad detected.";
        DrawPreviewGrid();
        RegisterModeHotkeys();
    }

    private bool TryRegisterHotkey(int id, Key key)
    {
        if (_source == null) return false;
        if (_registeredHotkeys.Contains(id)) return true;
        if (!RegisterHotKey(_source.Handle, id, MOD_NOREPEAT,
                (uint)KeyInterop.VirtualKeyFromKey(key)))
            return false; // another Windows app may already own this binding.
        _registeredHotkeys.Add(id);
        return true;
    }

    private void UnregisterHotkey(int id)
    {
        if (_source == null || !_registeredHotkeys.Remove(id)) return;
        UnregisterHotKey(_source.Handle, id);
    }

    private void RegisterModeHotkeys()
    {
        bool full = TryRegisterHotkey(HOTKEY_FULL, Key.F7);
        bool lanes = TryRegisterHotkey(HOTKEY_LANES, Key.F8);
        bool toggle = TryRegisterHotkey(HOTKEY_SWITCH, Key.F9);
        bool normal = TryRegisterHotkey(HOTKEY_NORMAL, Key.F10);
        if (!full || !lanes || !toggle || !normal)
            StatusText.Text += " Global shortcut conflict: use the on-screen mode buttons.";
    }

    private void UnregisterAllHotkeys()
    {
        foreach (int key in _registeredHotkeys.ToArray())
            UnregisterHotkey(key);
    }

    private void UpdateCursorVisibility()
    {
        try
        {
            if (!_enabled || !_cursorTarget())
            {
                if (_cursorVisibility.IsHidden)
                    _cursorVisibility.Restore();
                return;
            }
            if (!_cursorHideFailed)
                _cursorVisibility.Hide();
        }
        catch (Exception ex)
        {
            _cursorHideFailed = true;
            try { _cursorVisibility.Restore(); } catch { /* Crash guard also restores. */ }
            StatusText.Text = "Cursor hiding failed: " + ex.Message;
        }
    }

    private IntPtr WndProc(IntPtr window, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (message == WM_HOTKEY)
        {
            switch (wparam.ToInt32())
            {
                case HOTKEY_FULL:
                    _ = ActivateRhythmModeAsync(TouchMappingMode.FullScreen);
                    break;
                case HOTKEY_LANES:
                    _ = ActivateRhythmModeAsync(TouchMappingMode.GameLanes);
                    break;
                case HOTKEY_SWITCH:
                    SwitchRhythmMapping();
                    break;
                case HOTKEY_NORMAL:
                    ReturnToNormalMode();
                    break;
                case HOTKEY_EXIT:
                    Close();
                    break;
                default:
                    return IntPtr.Zero;
            }
            handled = true;
            return IntPtr.Zero;
        }
        if (message != TouchpadHelper.WM_INPUT)
            return IntPtr.Zero;

        // Windows PTP can send a single logical frame across several HID
        // reports, including count=0 continuations. Never send partial
        // frames to the touch bridge (they look like finger releases).
        IReadOnlyList<TouchpadContact[]> frames;
        try
        {
            frames = TouchpadHelper.ParseFrames(lparam);
        }
        catch (Exception ex)
        {
            // A malformed HID packet or unplugged device must not leave a
            // finger held inside Android, or terminate the WPF dispatcher.
            Disable("PTP decode failed: " + ex.Message);
            return IntPtr.Zero;
        }
        foreach (var contacts in frames)
        {
            if (!ProcessContactFrame(contacts))
                break;
        }

        // Leave handled=false: Windows needs its raw-input cleanup and may
        // still process system gestures. Do not suppress normal input.
        return IntPtr.Zero;
    }


    // This is the latency-sensitive HID path. No WPF drawing, window geometry
    // queries, or marker rendering is allowed before bridge.Send().
    private bool ProcessContactFrame(TouchpadContact[] contacts)
    {
        _lastContactReportUtc = DateTime.UtcNow;
        if (_enabled && _bridge?.Connected == true)
        {
            var frame = _mapping.Update(contacts, TouchpadHelper.MinX,
                TouchpadHelper.MaxX, TouchpadHelper.MinY, TouchpadHelper.MaxY);
            if (frame != null && !_bridge.Send(frame))
            {
                Disable("Touch transfer interrupted; rhythm mode stopped.");
                return false;
            }
        }

        // Keep only the newest complete snapshot. Visuals may skip frames;
        // the scrcpy touch stream may not.
        _latestContacts = contacts;
        _diagnosticsPending = true;
        if (_hostOverlay.Active)
            _overlayPending = true;
        return true;
    }

    private void DrawDiagnostics(TouchpadContact[] contacts)
    {
        ContactsBox.Text = contacts.Length == 0
            ? "(no fingers)"
            : string.Join(Environment.NewLine,
                contacts.Select(c =>
                {
                    var (sx, sy) = _mapping.Map(c, TouchpadHelper.MinX,
                        TouchpadHelper.MaxX, TouchpadHelper.MinY, TouchpadHelper.MaxY);
                    return $"Finger {c.ContactId,2}: pad=({c.X,4},{c.Y,4})  screen=({sx,4},{sy,4})";
                }));
        DrawPreviewContacts(contacts);
        ContactsBox.Text += $"\nPTP HID reports={TouchpadHelper.ValidHidReportCount}  " +
                            $"complete frames={TouchpadHelper.CompleteFrameCount}" +
                            $"\nBounds X={TouchpadHelper.MinX}..{TouchpadHelper.MaxX}  " +
                            $"Y={TouchpadHelper.MinY}..{TouchpadHelper.MaxY}" +
                             $"\nWindows mouse events suppressed={_mouseGuard.BlockedEvents}";
        if (_bridge is ScrcpyTouchBridge b)
            ContactsBox.Text += $"\nscrcpy: queued={b.FramesQueued} pumped={b.FramesPumped} packets={b.PacketsWritten}" +
                $" pending={b.PendingFrames} enqueue-to-pump={b.LastLocalQueueAgeUs / 1000.0:F2}ms" +
                $" peak={b.PeakLocalQueueAgeUs / 1000.0:F2}ms (host only)";
    }

    private void BackendChoice_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (EndpointBox == null) return;
        EndpointBox.Text = BackendChoice.SelectedIndex == 0
            ? "127.0.0.1:5555"
            : "http://127.0.0.1:8592";
    }

    // Show how the entire touchpad maps onto the actual landscape screen
    // even while the Android connection and game input bridge remain OFF.
    private void DrawPreviewGrid()
    {
        if (PreviewGrid == null) return;
        PreviewGrid.Children.Clear();
        const double pw = 576, ph = 324;
        for (int lane = 1; lane < 12; lane++)
        {
            double x = pw * lane / 12.0;
            PreviewGrid.Children.Add(new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = ph,
                Stroke = new SolidColorBrush(Color.FromRgb(62, 70, 111)),
                StrokeThickness = 0.9
            });
        }
        // Judgment reference for optional concentrated-lane mode, not an
        // assertion that every chart uses precisely this screen Y coordinate.
        PreviewGrid.Children.Add(new Line
        {
            X1 = 0, X2 = pw, Y1 = ph * 0.82, Y2 = ph * 0.82,
            Stroke = Brushes.MediumPurple,
            StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            Opacity = 0.8
        });
        var title = new TextBlock
        {
            Text = _mapping.Mode == TouchMappingMode.FullScreen
                ? "FULL SCREEN  •  absolute touch"
                : "GAME LANES  •  judgment zone",
            FontSize = 11,
            Foreground = Brushes.LightSteelBlue
        };
        Canvas.SetLeft(title, 12);
        Canvas.SetTop(title, 8);
        PreviewGrid.Children.Add(title);
    }

    private void DrawPreviewContacts(IReadOnlyList<TouchpadContact> contacts)
    {
        if (PreviewContacts == null) return;
        PreviewContacts.Children.Clear();
        foreach (var contact in contacts)
        {
            var (screenX, screenY) = _mapping.Map(contact,
                TouchpadHelper.MinX, TouchpadHelper.MaxX,
                TouchpadHelper.MinY, TouchpadHelper.MaxY);
            double cx = screenX * (575.0 / Math.Max(1, _mapping.ScreenWidth - 1));
            double cy = screenY * (323.0 / Math.Max(1, _mapping.ScreenHeight - 1));
            var circle = new Ellipse
            {
                Width = 16, Height = 16,
                Fill = Brushes.MediumPurple,
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(circle, Math.Clamp(cx - 8, 0, 560));
            Canvas.SetTop(circle, Math.Clamp(cy - 8, 0, 308));
            PreviewContacts.Children.Add(circle);

            var caption = new TextBlock
            {
                Text = $"{contact.ContactId} ({screenX},{screenY})",
                FontSize = 10,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(190, 24, 28, 54)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(caption, Math.Clamp(cx + 12, 0, 466));
            Canvas.SetTop(caption, Math.Clamp(cy - 12, 0, 308));
            PreviewContacts.Children.Add(caption);
        }
    }

    private TouchMapping CreateTouchMapping(bool mumu)
    {
        bool fullScreen = MappingChoice?.SelectedIndex == 1;
        double span = FlickChoice?.SelectedIndex switch
        {
            0 => 0.25,  // precise slide and deliberate flick motion
            2 => 0.43,  // shorter physical swipe still travels in game
            _ => 0.34,
        };
        return new TouchMapping
        {
            ScreenWidth = mumu ? 1920 : 1280,
            ScreenHeight = mumu ? 1080 : 720,
            Mode = fullScreen ? TouchMappingMode.FullScreen : TouchMappingMode.GameLanes,
            VerticalSpan = span,
        };
    }

    private void MappingChoice_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e) =>
        ApplyMappingChange("Touch layout");

    private void FlickChoice_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e) =>
        ApplyMappingChange("Flick sensitivity");

    private void ApplyMappingChange(string setting)
    {
        // Events may fire during InitializeComponent before all named fields exist.
        if (ToggleButton == null || StatusText == null ||
            MappingChoice == null || FlickChoice == null)
            return;

        if (_enabled)
            Disable(setting + " changed; active contacts released. Press F7/F8 to resume.");

        _mapping = CreateTouchMapping(BackendChoice?.SelectedIndex == 0);
        FlickChoice.IsEnabled = _mapping.Mode == TouchMappingMode.GameLanes;
        DrawPreviewGrid();
        PreviewContacts.Children.Clear();
        StatusText.Text = setting + ": " +
            (MappingChoice.SelectedIndex == 1 ? "full screen" : "12 lanes") + ".";
    }

    private async void Connect_Click(object sender, RoutedEventArgs e) =>
        await ConnectBridgeAsync();

    private async Task<bool> ConnectBridgeAsync()
    {
        if (_isClosing) return false;
        if (_bridge?.Connected == true) return true;
        if (_bridge != null || _connecting) return false;
        _connecting = true;
        ConnectButton.IsEnabled = false;
        EndpointBox.IsEnabled = false;
        BackendChoice.IsEnabled = false;
        try
        {
            bool mumu = BackendChoice.SelectedIndex == 0;
            _mapping = CreateTouchMapping(mumu);
            _bridge = mumu ? new ScrcpyTouchBridge() : new EmulatorTouchBridge();
            var connectedBridge = _bridge;
            connectedBridge.Faulted += ex => Dispatcher.BeginInvoke(
                () => _ = RecoverBridgeAsync(connectedBridge, ex));
            await connectedBridge.ConnectAsync(EndpointBox.Text.Trim());
            if (_isClosing || !ReferenceEquals(_bridge, connectedBridge)) return false;
            string touchDotsStatus = "";
            if (mumu && ShowTouchDotsCheck?.IsChecked == true)
            {
                try
                {
                    await _guestTouches.EnableAsync();
                    touchDotsStatus = " Touch points visible inside emulator.";
                }
                catch (Exception markerError)
                {
                    touchDotsStatus = " Android touch markers failed: " + markerError.Message;
                }
            }
            if (_isClosing || !ReferenceEquals(_bridge, connectedBridge))
            {
                try { await _guestTouches.RestoreAsync(); } catch { }
                return false;
            }
            _connecting = false;
            DisconnectButton.IsEnabled = true;
            ToggleButton.IsEnabled = _touchpadPresent;
            StatusText.Text = (mumu ? "MuMu Android 15 scrcpy" : "Google Emulator gRPC")
                              + " connected." + touchDotsStatus;
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Connection failed: " + ex.Message;
            try { await _guestTouches.RestoreAsync(); } catch { }
            if (_bridge != null)
            {
                await _bridge.DisposeAsync();
                _bridge = null;
            }
            _connecting = false;
            ConnectButton.IsEnabled = true;
            EndpointBox.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
            ToggleButton.IsEnabled = false;
            BackendChoice.IsEnabled = true;
            return false;
        }
    }

    // Global while SekaiPad is running, even when Project Sekai is focused.
    // F7 / F8 directly enter the requested game mode from normal Windows.
    private void FullMode_Click(object sender, RoutedEventArgs e) =>
        _ = ActivateRhythmModeAsync(TouchMappingMode.FullScreen);

    private void LanesMode_Click(object sender, RoutedEventArgs e) =>
        _ = ActivateRhythmModeAsync(TouchMappingMode.GameLanes);

    private void SwitchMode_Click(object sender, RoutedEventArgs e) =>
        SwitchRhythmMapping();

    private void NormalMode_Click(object sender, RoutedEventArgs e) =>
        ReturnToNormalMode();

    private void ExitMode_Click(object sender, RoutedEventArgs e) => Close();

    private void SwitchRhythmMapping()
    {
        TouchMappingMode other = _mapping.Mode == TouchMappingMode.FullScreen
            ? TouchMappingMode.GameLanes : TouchMappingMode.FullScreen;
        if (_enabled)
            _ = ActivateRhythmModeAsync(other);
        else
        {
            // Switching a preview while NORMAL must never start Android input
            // accidentally. F7 and F8 are the only entry hotkeys.
            MappingChoice.SelectedIndex = other == TouchMappingMode.FullScreen ? 1 : 0;
            StatusText.Text = "Preview mapping changed. Press F7 or F8 to enter rhythm mode.";
        }
    }

    private async Task ActivateRhythmModeAsync(TouchMappingMode mode)
    {
        if (_isClosing || _activatingRhythmMode) return;
        _activatingRhythmMode = true;
        try
        {
            if (!_touchpadPresent)
            {
                StatusText.Text = "No Windows Precision Touchpad detected.";
                return;
            }
            // Always send UP for existing fingers before remapping their
            // coordinates; a mode switch must never create ghost touches.
            if (_enabled)
                Disable("Switching rhythm mode: all previous contacts released.");
            MappingChoice.SelectedIndex = mode == TouchMappingMode.FullScreen ? 1 : 0;
            _mapping = CreateTouchMapping(BackendChoice.SelectedIndex == 0);
            DrawPreviewGrid();
            if (_bridge?.Connected != true && !await ConnectBridgeAsync())
            {
                StatusText.Text = "Rhythm mode could not start: emulator connection failed.";
                return;
            }
            if (_isClosing || _bridge?.Connected != true) return;
            EnterRhythmMode();
        }
        finally
        {
            _activatingRhythmMode = false;
        }
    }

    private async void ShowTouchDots_Changed(object sender, RoutedEventArgs e)
    {
        if (StatusText == null || _connecting ||
            _bridge?.Connected != true || BackendChoice?.SelectedIndex != 0)
            return;
        try
        {
            if (ShowTouchDotsCheck.IsChecked == true)
                await _guestTouches.EnableAsync();
            else
                await _guestTouches.RestoreAsync();
            StatusText.Text = ShowTouchDotsCheck.IsChecked == true
                ? "Android touch markers enabled."
                : "Android touch markers restored to their previous setting.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Android touch marker setting failed: " + ex.Message;
        }
    }

    private void HostTouchOverlay_Changed(object sender, RoutedEventArgs e)
    {
        if (HostTouchOverlayCheck == null || !_enabled || BackendChoice.SelectedIndex != 0)
            return;
        if (HostTouchOverlayCheck.IsChecked == true)
        {
            try { _hostOverlay.Enable(); _overlayPending = true; }
            catch (Exception ex) { StatusText.Text = "Overlay unavailable: " + ex.Message; }
        }
        else
        {
            _overlayPending = false;
            _hostOverlay.Disable();
        }
    }

    private void EnterRhythmMode()
    {
        if (!_touchpadPresent || _bridge?.Connected != true) return;
        if (!_registeredHotkeys.Contains(HOTKEY_NORMAL))
        {
            StatusText.Text = "Cannot enter rhythm mode: global F10 mouse-restore hotkey unavailable.";
            return;
        }
        if (!TryRegisterHotkey(HOTKEY_EXIT, Key.F6))
        {
            StatusText.Text =
                "F6 global exit is unavailable; rhythm input disabled for safety.";
            ModeStateText.Text = "NORMAL MODE - F6 hotkey is unavailable";
            return;
        }
        try
        {
            // System 3-/4-finger actions are handled *before* mouse messages;
            // the low-level mouse hook alone cannot suppress those gestures.
            _gestureGuard.Enable();
            // Lock ordinary mouse input while Raw HID continues into Android.
            _mouseGuard.Enable();
        }
        catch (Exception ex)
        {
            try { _mouseGuard.Disable(); } catch { }
            try { _gestureGuard.Restore(); } catch { }
            UnregisterHotkey(HOTKEY_EXIT);
            StatusText.Text = "Windows gesture/mouse suppression unavailable; game input OFF: " + ex.Message;
            ModeStateText.Text = "NORMAL MODE - gesture suppression unavailable";
            return;
        }
        _enabled = true;
        _cursorHideFailed = false;
        _lastContactReportUtc = DateTime.UtcNow;
        ToggleButton.Content = "Disable touch bridge";
        ModeStateText.Text = _mapping.Mode == TouchMappingMode.FullScreen
            ? "RHYTHM MODE - absolute full screen (F7)"
            : "RHYTHM MODE - 12-lane judgment zone (F8)";
        ModeStateText.Foreground = Brushes.MediumPurple;
        StatusText.Text =
            "Touchpad -> Android only. Windows pointer frozen; mouse clicks blocked. F10 normal, F6 exit.";
        UpdateCursorVisibility();
        if (BackendChoice.SelectedIndex == 0 && HostTouchOverlayCheck.IsChecked == true)
        {
            try { _hostOverlay.Enable(); _overlayPending = true; }
            catch (Exception ex)
            {
                StatusText.Text = "Mapped-touch overlay failed: " + ex.Message;
            }
        }
    }

    private void ReturnToNormalMode() =>
        Disable("F7/F8 enter rhythm mode.");

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_bridge is null || _connecting) return;
        await DisconnectBridgeAsync("Disconnected from emulator. Touch bridge OFF.");
    }

    private async Task RecoverBridgeAsync(ITouchBridge failingBridge, Exception error)
    {
        if (!ReferenceEquals(_bridge, failingBridge))
            return; // Ignore a late fault from a bridge that was already replaced.
        await DisconnectBridgeAsync("Connection lost: " + error.Message);
    }

    private async Task DisconnectBridgeAsync(string reason)
    {
        Disable(reason);
        try { await _guestTouches.RestoreAsync(); }
        catch (Exception ex) { StatusText.Text = reason + "; guest touch-dot restore failed: " + ex.Message; }
        ITouchBridge? previous = _bridge;
        _bridge = null;
        ToggleButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        try
        {
            if (previous != null)
                await previous.DisposeAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Disconnect error: " + ex.Message;
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            BackendChoice.IsEnabled = true;
            EndpointBox.IsEnabled = true;
        }
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_enabled)
        {
            Disable("Touch bridge off");
        }
        else if (_bridge?.Connected == true)
            EnterRhythmMode();
    }

    private void Release_Click(object sender, RoutedEventArgs e) => Disable("All contacts released");

    private void ReleaseAll(string reason)
    {
        var up = _mapping.ReleaseAll();
        if (up != null && _bridge?.Connected == true)
            _bridge.Send(up);
        _latestContacts = Array.Empty<TouchpadContact>();
        _diagnosticsPending = true;
        if (_hostOverlay.Active)
            _overlayPending = true;
        StatusText.Text = reason;
    }

    private void Disable(string reason)
    {
        _enabled = false;
        _overlayPending = false;
        try { _hostOverlay.Disable(); } catch { }
        string? mouseError = null;
        try { _mouseGuard.Disable(); }
        catch (Exception ex) { mouseError = ex.Message; }
        string? gestureError = null;
        try { _gestureGuard.Restore(); }
        catch (Exception ex) { gestureError = ex.Message; }
        string? cursorError = null;
        try { _cursorVisibility.Restore(); }
        catch (Exception ex) { cursorError = ex.Message; }
        _cursorHideFailed = false;
        UnregisterHotkey(HOTKEY_EXIT); // normal Windows F6 remains available
        TouchpadHelper.ResetPendingReports();
        string isolationError = mouseError == null ? "" : "; mouse restore failed: " + mouseError;
        if (gestureError != null) isolationError += "; Windows gesture restore failed: " + gestureError;
        if (cursorError != null) isolationError += "; cursor restore failed: " + cursorError;
        ReleaseAll(reason + isolationError);
        ToggleButton.Content = "Enable touch bridge";
        if (ModeStateText != null)
        {
            ModeStateText.Text =
                "NORMAL MODE - Windows touchpad active - Android touch input OFF";
            ModeStateText.Foreground = Brushes.DarkSlateGray;
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        // WPF may exit its dispatcher immediately after the last window closes.
        // Finish the queued touch UP packets before allowing that shutdown.
        if (_readyToClose || _bridge == null)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_isClosing) return;
        _isClosing = true;
        Disable("Flushing touch releases before exit...");
        _watchdog.Stop();
        _visualTimer.Stop();
        ITouchBridge active = _bridge;
        _bridge = null;
        try
        {
            try { await _guestTouches.RestoreAsync(); }
            catch (Exception ex) { StatusText.Text = "Guest touch-dot restore failed: " + ex.Message; }
            await active.DisposeAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Touch bridge close error: " + ex.Message;
        }
        finally
        {
            _readyToClose = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Disable("Shutting down");
        _watchdog.Stop();
        _visualTimer.Stop();
        UnregisterAllHotkeys();
        try { _mouseGuard.Disable(); } catch { }
        try { _gestureGuard.Restore(); } catch { }
        try { _hostOverlay.Disable(); } catch { }
        _source?.RemoveHook(WndProc);
        _bridge = null;
        base.OnClosed(e);
    }
}

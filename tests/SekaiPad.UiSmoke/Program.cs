using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Android.Emulation.Control;
using RawInput.Touchpad;
using SekaiPad;

/// <summary>
/// Tests WPF close handling with a fake transport. Never launches Android,
/// connects to ADB or touches game/login screens.
/// </summary>
internal sealed class FakeBridge : ITouchBridge
{
    public bool Connected { get; private set; } = true;
    public bool Disposed { get; private set; }
    public List<TouchEvent> Sent { get; } = new();
    public event Action<Exception>? Faulted
    {
        add { }
        remove { }
    }

    public Task ConnectAsync(string endpoint) => Task.CompletedTask;

    public bool Send(TouchEvent evt)
    {
        if (!Connected) return false;
        Sent.Add(evt);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(330);
        Disposed = true;
        Connected = false;
    }
}

internal sealed class FakeCursorVisibility : ICursorVisibility
{
    public bool IsHidden { get; private set; }
    public int HideCount { get; private set; }
    public int RestoreCount { get; private set; }
    public void Hide() { IsHidden = true; HideCount++; }
    public void Restore() { IsHidden = false; RestoreCount++; }
}

internal sealed class FakeMouseGuard : IMouseInputGuard
{
    public bool IsActive { get; private set; }
    public long BlockedEvents => 0;
    public bool FailEnable { get; set; }
    public int EnableCount { get; private set; }
    public int DisableCount { get; private set; }
    public void Enable()
    {
        EnableCount++;
        if (FailEnable) throw new IOException("Synthetic hook installation failure");
        IsActive = true;
    }
    public void Disable() { IsActive = false; DisableCount++; }
}

internal sealed class FakeGestureGuard : IWindowsGestureGuard
{
    public bool IsActive { get; private set; }
    public bool FailEnable { get; set; }
    public int RestoreCount { get; private set; }

    public void Enable()
    {
        if (FailEnable) throw new IOException("Synthetic OS gesture cache failure");
        IsActive = true;
    }

    public void Restore() { IsActive = false; RestoreCount++; }
}

internal sealed class FakeHostTouchOverlay : IHostTouchOverlay
{
    public bool Active { get; private set; }
    public int EnabledCount { get; private set; }
    public int DisabledCount { get; private set; }
    public int RenderCount { get; private set; }
    public int RefreshedCount { get; private set; }

    public void Enable() { Active = true; EnabledCount++; }
    public void Refresh() { if (Active) RefreshedCount++; }
    public void Render(IReadOnlyList<TouchpadContact> contacts, TouchMapping mapping,
        int minX, int maxX, int minY, int maxY)
    {
        if (Active) RenderCount++;
    }
    public void Disable() { Active = false; DisabledCount++; }
}

internal static class Program
{
    private static bool _manualDisconnectPassed;
    private static bool _faultRecoveryPassed;
    private static bool _simulateOccupiedF6;

    private static void ReserveFakeExitBinding(MainWindow window)
    {
        if (!_simulateOccupiedF6) return;
        var field = typeof(MainWindow).GetField("_registeredHotkeys",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var fakeBindings = (HashSet<int>)field.GetValue(window)!;
        fakeBindings.Add(0x5E74); // F10 is reserved by the user's running instance.
        fakeBindings.Add(0x5E75); // F6 is reserved by the user's running instance.
    }

    private static void StartSecondPhase(Application app)
    {
        var second = new MainWindow();
        var fake = new FakeBridge();
        second.Show();
        var bridgeField = typeof(MainWindow).GetField("_bridge",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("UI bridge reflection failed");
        var mapField = typeof(MainWindow).GetField("_mapping",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("UI mapping reflection failed");
        bridgeField.SetValue(second, fake);
        var hostOverlay = new FakeHostTouchOverlay();
        typeof(MainWindow).GetField("_hostOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(second, hostOverlay);
        ((System.Windows.Controls.CheckBox)second.FindName("HostTouchOverlayCheck"))
            .IsChecked = true; // Optional diagnostic overlay opt-in.

        var modeCursor = new FakeCursorVisibility();
        var mouseGuard = new FakeMouseGuard();
        var bindings = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(MainWindow).GetField("_mouseGuard", bindings)!.SetValue(second, mouseGuard);
        var gestureGuard = new FakeGestureGuard();
        typeof(MainWindow).GetField("_gestureGuard", bindings)!.SetValue(second, gestureGuard);
        typeof(MainWindow).GetField("_cursorVisibility", bindings)!.SetValue(second, modeCursor);
        typeof(MainWindow).GetField("_cursorTarget", bindings)!.SetValue(second, (Func<bool>)(() => true));
        typeof(MainWindow).GetField("_touchpadPresent", bindings)!.SetValue(second, true);
        ReserveFakeExitBinding(second);
        typeof(MainWindow).GetMethod("EnterRhythmMode", bindings)!.Invoke(second, null);
        if (!modeCursor.IsHidden || !mouseGuard.IsActive || !gestureGuard.IsActive || !hostOverlay.Active)
            throw new Exception("Cursor, mouse, OS gestures and host overlay not enabled on rhythm entry");

        ((TouchMapping)(mapField.GetValue(second)!)).Update(
            new[] { new TouchpadContact(8, 700, 1900) }, 0, 4010, 0, 2468);

        var disconnect = typeof(MainWindow).GetMethod("Disconnect_Click",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("UI Disconnect_Click missing");

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        var elapsed = Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(3))
                throw new TimeoutException("WPF manual disconnect timed out");
            if (!fake.Disposed) return;
            timer.Stop();
            var connect = (System.Windows.Controls.Button)second.FindName("ConnectButton");
            var disconnectButton =
                (System.Windows.Controls.Button)second.FindName("DisconnectButton");
            var toggle = (System.Windows.Controls.Button)second.FindName("ToggleButton");
            var backend = (System.Windows.Controls.ComboBox)second.FindName("BackendChoice");
            var endpoint = (System.Windows.Controls.TextBox)second.FindName("EndpointBox");

            if (modeCursor.IsHidden || mouseGuard.IsActive || gestureGuard.IsActive || hostOverlay.Active)
                throw new Exception("Cursor/mouse/OS gesture/host overlay not restored on disconnect");
            if (!connect.IsEnabled || disconnectButton.IsEnabled || toggle.IsEnabled ||
                !backend.IsEnabled || !endpoint.IsEnabled ||
                bridgeField.GetValue(second) != null)
                throw new Exception("UI controls not reset after manual disconnect");
            if (fake.Sent.Count != 1 ||
                fake.Sent[0].Touches.Single().Pressure != 0)
                throw new Exception("Manual disconnect lost synthetic touch UP");

            _manualDisconnectPassed = true;
            Console.WriteLine(
                $"WPF_MANUAL_DISCONNECT_PASS {elapsed.ElapsedMilliseconds}ms " +
                "controls_recovered=true up_frames=1");
            second.Close();
            StartThirdPhase(app);
        };
        timer.Start();
        disconnect.Invoke(second, new object?[] { second, new RoutedEventArgs() });
    }

    private static void StartThirdPhase(Application app)
    {
        var third = new MainWindow();
        var fake = new FakeBridge();
        third.Show();
        var binding = BindingFlags.Instance | BindingFlags.NonPublic;
        var bridgeField = typeof(MainWindow).GetField("_bridge", binding)!;
        var mapField = typeof(MainWindow).GetField("_mapping", binding)!;
        var recovery = typeof(MainWindow).GetMethod("RecoverBridgeAsync", binding)
            ?? throw new Exception("UI RecoverBridgeAsync missing");
        bridgeField.SetValue(third, fake);
        var hostOverlay = new FakeHostTouchOverlay();
        typeof(MainWindow).GetField("_hostOverlay", binding)!.SetValue(third, hostOverlay);
        ((System.Windows.Controls.CheckBox)third.FindName("HostTouchOverlayCheck"))
            .IsChecked = true; // Optional diagnostic overlay opt-in.

        var modeCursor = new FakeCursorVisibility();
        var mouseGuard = new FakeMouseGuard();
        typeof(MainWindow).GetField("_mouseGuard", binding)!.SetValue(third, mouseGuard);
        var gestureGuard = new FakeGestureGuard();
        typeof(MainWindow).GetField("_gestureGuard", binding)!.SetValue(third, gestureGuard);
        typeof(MainWindow).GetField("_cursorVisibility", binding)!.SetValue(third, modeCursor);
        typeof(MainWindow).GetField("_cursorTarget", binding)!.SetValue(third, (Func<bool>)(() => true));
        typeof(MainWindow).GetField("_touchpadPresent", binding)!.SetValue(third, true);
        ReserveFakeExitBinding(third);
        typeof(MainWindow).GetMethod("EnterRhythmMode", binding)!.Invoke(third, null);
        if (!modeCursor.IsHidden || !mouseGuard.IsActive || !gestureGuard.IsActive || !hostOverlay.Active)
            throw new Exception("Cursor/mouse/gesture/overlay guards were not engaged before fault");

        ((TouchMapping)mapField.GetValue(third)!).Update(
            new[] { new TouchpadContact(9, 900, 1200) }, 0, 4010, 0, 2468);
        var recoverTask = (Task)(recovery.Invoke(third,
            new object?[] { fake, new IOException("Simulated broken control channel") })
            ?? throw new Exception("Fault handler did not return a Task"));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        var elapsed = Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(3))
                throw new TimeoutException("WPF fault recovery timed out");
            if (!recoverTask.IsCompleted) return;
            timer.Stop();
            recoverTask.GetAwaiter().GetResult();
            if (modeCursor.IsHidden || mouseGuard.IsActive || gestureGuard.IsActive || hostOverlay.Active)
                throw new Exception("Cursor/mouse/gesture/overlay input not restored after bridge fault");
            if (!fake.Disposed ||
                bridgeField.GetValue(third) != null ||
                fake.Sent.Count != 1 ||
                fake.Sent[0].Touches.Single().Pressure != 0)
                throw new Exception("Fault cleanup failed to release the contact");
            var connect = (System.Windows.Controls.Button)third.FindName("ConnectButton");
            var toggle = (System.Windows.Controls.Button)third.FindName("ToggleButton");
            var backend = (System.Windows.Controls.ComboBox)third.FindName("BackendChoice");
            if (!connect.IsEnabled || toggle.IsEnabled || !backend.IsEnabled)
                throw new Exception("Fault did not restore reconnect controls");

            // A late exception from the old bridge must not disturb the
            // restored UI once the corresponding transport has been removed.
            var staleTask = (Task)recovery.Invoke(third, new object?[] {
                fake, new IOException("Late fault after disposal")
            })!;
            if (!staleTask.IsCompletedSuccessfully)
                throw new Exception("Stale fault should be ignored immediately");

            _faultRecoveryPassed = true;
            Console.WriteLine(
                $"WPF_FAULT_RECOVERY_PASS {elapsed.ElapsedMilliseconds}ms " +
                "touches_released=true reconnect_ready=true");
            third.Close();
            app.Shutdown();
        };
        timer.Start();
    }

    [STAThread]
    public static int Main(string[] args)
    {
        _simulateOccupiedF6 = args.Contains("--occupied-global-f6");
        if (_simulateOccupiedF6)
            Console.WriteLine("UI smoke uses synthetic F6 because another process owns the actual global binding.");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        var fake = new FakeBridge();

        var bridgeField = typeof(MainWindow).GetField("_bridge",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("UI _bridge field missing");
        var mappingField = typeof(MainWindow).GetField("_mapping",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("UI _mapping field missing");
        window.Show();
        var hostOverlay = new FakeHostTouchOverlay();
        typeof(MainWindow).GetField("_hostOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, hostOverlay);
        var hostOverlayCheck = (System.Windows.Controls.CheckBox)window.FindName("HostTouchOverlayCheck");
        if (hostOverlayCheck.IsChecked != false)
            throw new Exception("Diagnostic host overlay must default off for low-latency play");
        hostOverlayCheck.IsChecked = true; // Explicitly enable for overlay lifecycle tests.
        var (lx, ly) = (MuMuHostTouchOverlay.MapGameToClient(0, 0, 1920, 1080, 1280, 720).X,
                        MuMuHostTouchOverlay.MapGameToClient(0, 0, 1920, 1080, 1280, 720).Y);
        var mid = MuMuHostTouchOverlay.MapGameToClient(960, 540, 1920, 1080, 1280, 720);
        var far = MuMuHostTouchOverlay.MapGameToClient(1919, 1079, 1920, 1080, 1280, 720);
        if (lx != 0 || ly != 0 || Math.Abs(mid.X - 640) > 1 || Math.Abs(mid.Y - 360) > 1 ||
            far != new OverlayPixel(1279, 719))
            throw new Exception("Host overlay game-to-renderer coordinate mapping was incorrect");
        Console.WriteLine("HOST_OVERLAY_GEOMETRY_PASS 1920x1080 -> 1280x720");
        var mappingChoice = (System.Windows.Controls.ComboBox)window.FindName("MappingChoice");
        var flickChoice = (System.Windows.Controls.ComboBox)window.FindName("FlickChoice");
        var touchDots = (System.Windows.Controls.CheckBox)window.FindName("ShowTouchDotsCheck");
        if (touchDots.IsChecked != true) throw new Exception("Emulator tap marker option must default ON");
        var previewGrid = (System.Windows.Controls.Canvas)window.FindName("PreviewGrid");
        var previewContacts = (System.Windows.Controls.Canvas)window.FindName("PreviewContacts");
        var previewMethod = typeof(MainWindow).GetMethod("DrawPreviewContacts",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (mappingChoice.SelectedIndex != 1 || flickChoice.IsEnabled ||
            previewGrid.Children.Count < 13 || previewMethod == null)
            throw new Exception("Absolute coordinate mode is not the verified UI default");
        previewMethod.Invoke(window, new object[] {
            new TouchpadContact[] {
                new(1, 0, 0), new(2, 4010, 2468)
            }
        });
        var markers = previewContacts.Children.OfType<System.Windows.Shapes.Ellipse>().ToArray();
        if (markers.Length != 2 ||
            System.Windows.Controls.Canvas.GetLeft(markers[0]) >
                System.Windows.Controls.Canvas.GetLeft(markers[1]) ||
            System.Windows.Controls.Canvas.GetTop(markers[0]) >
                System.Windows.Controls.Canvas.GetTop(markers[1]))
            throw new Exception("Visual absolute-coordinate markers did not map both corners");
        Console.WriteLine("WPF_ABSOLUTE_PREVIEW_PASS default full-screen, 12-lane grid, corner markers");
        var exitButton = window.FindName("ExitModeButton") as System.Windows.Controls.Button;
        if (exitButton?.Content?.ToString() != "F6  Exit app")
            throw new Exception("F6 exit button missing or incorrectly labeled");
        var binding = BindingFlags.Instance | BindingFlags.NonPublic;
        var registeredHotkeys = (HashSet<int>)(typeof(MainWindow)
            .GetField("_registeredHotkeys", binding)!.GetValue(window)
            ?? throw new Exception("Global hotkey state missing"));
        if (registeredHotkeys.Contains(0x5E75))
            throw new Exception("F6 should not be reserved in normal Windows mode");
        bridgeField.SetValue(window, fake);
        var modeCursor = new FakeCursorVisibility();
        var mouseGuard = new FakeMouseGuard { FailEnable = true };
        typeof(MainWindow).GetField("_mouseGuard", binding)!.SetValue(window, mouseGuard);
        var gestureGuard = new FakeGestureGuard { FailEnable = true };
        typeof(MainWindow).GetField("_gestureGuard", binding)!.SetValue(window, gestureGuard);
        var cursorTargetField = typeof(MainWindow).GetField("_cursorTarget", binding)!;
        typeof(MainWindow).GetField("_cursorVisibility", binding)!.SetValue(window, modeCursor);
        cursorTargetField.SetValue(window, (Func<bool>)(() => true));
        typeof(MainWindow).GetField("_touchpadPresent", binding)!.SetValue(window, true);
        ReserveFakeExitBinding(window);
        typeof(MainWindow).GetMethod("EnterRhythmMode", binding)!.Invoke(window, null);
        if (registeredHotkeys.Contains(0x5E75) || mouseGuard.IsActive || gestureGuard.IsActive)
            throw new Exception("Failed OS gesture guard left rhythm mode partly enabled");
        gestureGuard.FailEnable = false;
        ReserveFakeExitBinding(window);
        typeof(MainWindow).GetMethod("EnterRhythmMode", binding)!.Invoke(window, null);
        if (gestureGuard.IsActive || registeredHotkeys.Contains(0x5E75))
            throw new Exception("Mouse install failure left system gestures disabled");
        if (registeredHotkeys.Contains(0x5E75) || mouseGuard.IsActive ||
            ((System.Windows.Controls.TextBlock)window.FindName("ModeStateText")).Text.Contains("RHYTHM"))
            throw new Exception("Failed mouse guard did not leave the touch bridge safely OFF");
        mouseGuard.FailEnable = false;
        ReserveFakeExitBinding(window);
        typeof(MainWindow).GetMethod("EnterRhythmMode", binding)!.Invoke(window, null);
        if (!mouseGuard.IsActive || !gestureGuard.IsActive || !hostOverlay.Active)
            throw new Exception("Rhythm mode did not enable input isolation and overlay");
        Console.WriteLine("WPF_MOUSE_GUARD_FAIL_CLOSED_PASS");
        if (!registeredHotkeys.Contains(0x5E75))
            throw new Exception("F6 global exit not registered in rhythm mode");
        if (!modeCursor.IsHidden)
            throw new Exception("Cursor should hide while rhythm mode and emulator focused");
        cursorTargetField.SetValue(window, (Func<bool>)(() => false));
        typeof(MainWindow).GetMethod("UpdateCursorVisibility", binding)!.Invoke(window, null);
        if (modeCursor.IsHidden)
            throw new Exception("Cursor not restored after leaving MuMu focus/pointer");
        cursorTargetField.SetValue(window, (Func<bool>)(() => true));
        typeof(MainWindow).GetMethod("UpdateCursorVisibility", binding)!.Invoke(window, null);
        if (!modeCursor.IsHidden)
            throw new Exception("Cursor did not hide again on returning to MuMu");
        typeof(MainWindow).GetMethod("ReturnToNormalMode", binding)!.Invoke(window, null);
        if (registeredHotkeys.Contains(0x5E75))
            throw new Exception("F6 global exit was not released in normal mode");
        if (modeCursor.IsHidden || mouseGuard.IsActive || gestureGuard.IsActive || hostOverlay.Active)
            throw new Exception("Mouse cursor, mouse guard, Windows gestures, overlay not restored by F10");
        Console.WriteLine("HOST_OVERLAY_LIFECYCLE_PASS rhythm/F10/disconnect/fault");
        Console.WriteLine("WPF_OS_GESTURE_GUARD_PASS F7/F8 and F10/fault/disconnect");
        Console.WriteLine("WPF_MOUSE_GUARD_LIFECYCLE_PASS F7 / F10 / fault / disconnect / close");
        Console.WriteLine("WPF_CURSOR_RESTORE_PASS MuMu focus / F10 / disconnect / fault");
        Console.WriteLine("WPF_F6_HOTKEY_PASS registered only during rhythm mode");

        // For latency correctness, the fake Android transport must receive a
        // complete contact before ANY diagnostic visual rendering.
        ReserveFakeExitBinding(window);
        typeof(MainWindow).GetMethod("EnterRhythmMode", binding)!.Invoke(window, null);
        var probeBridge = new FakeBridge();
        bridgeField.SetValue(window, probeBridge);
        int baselineRenders = hostOverlay.RenderCount;
        var handleFrame = typeof(MainWindow).GetMethod("ProcessContactFrame", binding)!;
        bool accepted = (bool)handleFrame.Invoke(window, new object[] {
            new TouchpadContact[] { new(13, 1200, 1100) }
        })!;
        if (!accepted || probeBridge.Sent.Count != 1 ||
            hostOverlay.RenderCount != baselineRenders)
            throw new Exception("HID send must precede deferred host marker rendering");
        typeof(MainWindow).GetMethod("RefreshVisuals", binding)!.Invoke(window, null);
        if (hostOverlay.RenderCount != baselineRenders + 1)
            throw new Exception("Deferred marker rendering was not dispatched");
        handleFrame.Invoke(window, new object[] { Array.Empty<TouchpadContact>() });
        if (probeBridge.Sent.Count != 2 ||
            probeBridge.Sent[1].Touches.Single().Pressure != 0)
            throw new Exception("Immediate HID finger release missing");
        typeof(MainWindow).GetMethod("RefreshVisuals", binding)!.Invoke(window, null);
        if (hostOverlay.RenderCount != baselineRenders + 2)
            throw new Exception("Deferred release marker update missing");
        Console.WriteLine("WPF_INPUT_BEFORE_VISUALS_PASS synthetic DOWN/UP with delayed overlay");
        typeof(MainWindow).GetMethod("ReturnToNormalMode", binding)!.Invoke(window, null);
        bridgeField.SetValue(window, fake);
        var mapping = (TouchMapping)(mappingField.GetValue(window)
                       ?? throw new Exception("UI mapping missing"));
        mapping.Update(new[] { new TouchpadContact(7, 1200, 1400) },
                       0, 4010, 0, 2468);
        if (mapping.ActiveCount != 1)
            throw new Exception("Did not set up synthetic held contact");

        var stopwatch = Stopwatch.StartNew();
        bool closed = false;
        window.Closed += (_, _) =>
        {
            closed = true;
            if (!fake.Disposed)
                throw new Exception("WPF closed before transport flush finished");
            if (fake.Sent.Count != 1 ||
                fake.Sent[0].Touches.Single().Pressure != 0)
                throw new Exception("Held pointer was not released before close");
            if (stopwatch.ElapsedMilliseconds < 250)
                throw new Exception("WPF close was not deferred until drain");
            Console.WriteLine(
                $"WPF_CLOSE_FLUSH_PASS {stopwatch.ElapsedMilliseconds}ms " +
                $"disposal_complete={fake.Disposed} up_frames={fake.Sent.Count}");
            StartSecondPhase(app);
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!closed)
                throw new TimeoutException("WPF close flush timeout");
        };
        timer.Start();

        window.Dispatcher.BeginInvoke(new Action(window.Close),
            DispatcherPriority.ApplicationIdle);
        app.Run();
        if (!closed) throw new Exception("Closed event never fired");
        if (!_manualDisconnectPassed) throw new Exception("Manual disconnect unverified");
        if (!_faultRecoveryPassed) throw new Exception("Fault recovery unverified");
        return 0;
    }
}
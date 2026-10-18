using System.Diagnostics;
using System.IO;

namespace SekaiPad;

public interface IWindowsGestureGuard
{
    bool IsActive { get; }
    void Enable();
    void Restore();
}

public sealed class WindowsGestureGuard : IWindowsGestureGuard
{
    private readonly string _backupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SekaiPad", "gesture-guard",
        $"gesture-{Environment.ProcessId}-{Guid.NewGuid():N}.json");

    private bool _active;
    private Process? _watchdog;
    public bool IsActive => _active;

    public WindowsGestureGuard()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Restore(); }
            catch { /* Independent cursor/gesture guard owns crash recovery. */ }
        };
    }

    private void StartWatchdog()
    {
        if (_watchdog != null)
        {
            if (_watchdog.HasExited)
                throw new IOException("Gesture restoration watchdog exited unexpectedly.");
            return; // One watchdog per SekaiPad lifetime, not one per F7/F9.
        }
        string guard = Path.Combine(AppContext.BaseDirectory, "SekaiPad.CursorGuard.exe");
        if (!File.Exists(guard))
            throw new FileNotFoundException("Windows gesture restoration watchdog is absent.", guard);
        var ready = _backupPath + ".ready";
        var info = new ProcessStartInfo(guard)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("--gesture");
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        info.ArgumentList.Add(Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());
        info.ArgumentList.Add(_backupPath);
        info.ArgumentList.Add(ready);
        var guardProcess = Process.Start(info)
            ?? throw new IOException("Could not start Windows gesture restoration watchdog.");
        var timer = Stopwatch.StartNew();
        while (!File.Exists(ready) && !guardProcess.HasExited &&
               timer.Elapsed < TimeSpan.FromSeconds(3))
            Thread.Sleep(25);
        if (!File.Exists(ready) || guardProcess.HasExited)
        {
            guardProcess.Dispose();
            throw new IOException("Windows gesture restoration watchdog did not become ready.");
        }
        _watchdog = guardProcess;
    }

    public void Enable()
    {
        if (_active) return;
        // If the Windows build is not verified, do not even leave a crash
        // backup or start the watchdog: no system settings are touched.
        // The COM probe later provides an independent pre-write check.
        int build = Environment.OSVersion.Version.Build;
        if (!ImmersiveTouchpadSettingsCache.IsVerifiedShellBuild(build))
            throw new NotSupportedException(
                $"Windows build {build} is unverified for native gesture suppression.");
        // Back up original registry entries, including absence, before changing
        // any value. The independent guard restores them if the app crashes.
        var original = GestureSettings.Capture();
        GestureSettings.Save(_backupPath, original);
        try
        {
            StartWatchdog();
            GestureSettings.Suppress(); // refreshes Explorer's live cache
            _active = true;
        }
        catch
        {
            // Never start rhythm touch input without a working gesture guard.
            try { GestureSettings.RestoreFile(_backupPath); } catch { }
            throw;
        }
    }

    public void Restore()
    {
        if (!_active && !File.Exists(_backupPath)) return;
        GestureSettings.RestoreFile(_backupPath);
        _active = false;
    }
}

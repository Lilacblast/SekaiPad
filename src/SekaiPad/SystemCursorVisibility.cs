using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SekaiPad;

public interface ICursorVisibility
{
    bool IsHidden { get; }
    void Hide();
    void Restore();
}

// Unlike WPF Mouse.OverrideCursor or ShowCursor, this also affects the
// emulator's separate Windows process. Only use it while the game is under
// the pointer; every mutation has an in-process restore and a crash guard.
internal sealed class SystemCursorVisibility : ICursorVisibility
{
    private static readonly uint[] SystemCursorIds =
    {
        32512, 32513, 32514, 32515, 32516,
        32642, 32643, 32644, 32645, 32646,
        32648, 32649, 32650, 32651
    };

    private static readonly byte[] TransparentAndMask =
        Enumerable.Repeat((byte)0xff, 128).ToArray();
    private static readonly byte[] TransparentXorMask = new byte[128];

    private readonly object _gate = new();
    private readonly Dictionary<uint, IntPtr> _originals = new();
    private readonly string _activeMarker = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SekaiPad", "cursor-guard", $"cursor-{Environment.ProcessId}-{Guid.NewGuid():N}.active");
    // Keep the watchdog process handle alive. A bare boolean cannot detect
    // that a crash guard died while the main app is still running.
    private Process? _watchdog;
    private bool _hidden;

    public bool IsHidden
    {
        get { lock (_gate) return _hidden; }
    }

    [DllImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CopyIcon(IntPtr icon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyCursor(IntPtr cursor);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? module);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateCursor(
        IntPtr instance, int hotX, int hotY, int width, int height,
        byte[] andMask, byte[] xorMask);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetSystemCursor(IntPtr cursor, uint id);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfo(
        uint action, uint param, IntPtr result, uint flags);

    private const uint SPI_SETCURSORS = 0x0057;

    public SystemCursorVisibility()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Restore(); }
            catch { SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0); }
        };
    }

    internal static bool IsWatchdogAlive(Process? watchdog)
    {
        if (watchdog == null) return false;
        try { return !watchdog.HasExited; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private void EnsureGuard()
    {
        if (_watchdog != null)
        {
            if (!IsWatchdogAlive(_watchdog))
                throw new IOException("Cursor crash-recovery watchdog exited unexpectedly.");
            return;
        }

        string guard = Path.Combine(AppContext.BaseDirectory, "SekaiPad.CursorGuard.exe");
        if (!File.Exists(guard))
            throw new FileNotFoundException("Cursor restoration watchdog is missing.", guard);

        Directory.CreateDirectory(Path.GetDirectoryName(_activeMarker)!);
        File.WriteAllText(_activeMarker, "active", Encoding.ASCII);
        string ready = _activeMarker + ".ready";

        var startInfo = new ProcessStartInfo(guard)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());
        startInfo.ArgumentList.Add(_activeMarker);
        startInfo.ArgumentList.Add(ready);

        Process? candidate = null;
        try
        {
            candidate = Process.Start(startInfo)
                ?? throw new IOException("Cannot start the cursor watchdog.");
            var timer = Stopwatch.StartNew();
            while (!File.Exists(ready) && IsWatchdogAlive(candidate) &&
                   timer.Elapsed < TimeSpan.FromSeconds(3))
                Thread.Sleep(25);
            if (!File.Exists(ready) || !IsWatchdogAlive(candidate))
                throw new IOException("Cursor watchdog did not become ready.");
            _watchdog = candidate; // ownership transfers for the app lifetime
            candidate = null;
        }
        catch
        {
            candidate?.Dispose();
            TryDelete(_activeMarker);
            TryDelete(ready);
            throw;
        }
    }

    public void Hide()
    {
        lock (_gate)
        {
            if (_hidden)
            {
                if (!IsWatchdogAlive(_watchdog))
                {
                    // A live heartbeat is mandatory while system cursors have
                    // been replaced. Attempt immediate recovery; do not keep
                    // running in an unguarded hidden-cursor state.
                    RestoreCore();
                    throw new IOException(
                        "Cursor watchdog stopped; system cursors were restored.");
                }
                return;
            }
            EnsureGuard();
            // The guard watches this marker after any unexpected process exit.
            File.WriteAllText(_activeMarker, "active", Encoding.ASCII);
            try
            {
                foreach (uint id in SystemCursorIds)
                {
                    IntPtr system = LoadCursor(IntPtr.Zero, new IntPtr(id));
                    if (system == IntPtr.Zero)
                        throw new System.ComponentModel.Win32Exception(
                            Marshal.GetLastWin32Error(), $"LoadCursor({id}) failed");
                    IntPtr copy = CopyIcon(system);
                    if (copy == IntPtr.Zero)
                        throw new System.ComponentModel.Win32Exception(
                            Marshal.GetLastWin32Error(), $"CopyIcon({id}) failed");
                    _originals.Add(id, copy);
                }

                IntPtr module = GetModuleHandle(null);
                foreach (uint id in SystemCursorIds)
                {
                    IntPtr blank = CreateCursor(module, 0, 0, 32, 32,
                        TransparentAndMask, TransparentXorMask);
                    if (blank == IntPtr.Zero)
                        throw new System.ComponentModel.Win32Exception(
                            Marshal.GetLastWin32Error(), "CreateCursor failed");
                    if (!SetSystemCursor(blank, id))
                    {
                        int error = Marshal.GetLastWin32Error();
                        DestroyCursor(blank); // Success transfers ownership to Windows.
                        throw new System.ComponentModel.Win32Exception(
                            error, $"SetSystemCursor({id}) failed");
                    }
                }
                _hidden = true;
            }
            catch
            {
                RestoreCore();
                throw;
            }
        }
    }

    public void Restore()
    {
        lock (_gate) RestoreCore();
    }

    private void RestoreCore()
    {
        bool anyFailure = false;
        foreach (var (id, original) in _originals)
        {
            if (!SetSystemCursor(original, id))
            {
                anyFailure = true;
                DestroyCursor(original);
            }
        }
        _originals.Clear();
        _hidden = false;
        if (anyFailure && !SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0))
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(), "Unable to restore system cursors");
        TryDelete(_activeMarker);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class EmulatorPointerRegion
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        public readonly int X;
        public readonly int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

    public static bool IsMuMuFocusedUnderPointer()
    {
        if (!GetCursorPos(out Point position)) return false;
        IntPtr foreground = GetForegroundWindow();
        IntPtr hovered = WindowFromPoint(position);
        return IsMuMuWindow(foreground) && IsMuMuWindow(hovered);
    }

    private static bool IsMuMuWindow(IntPtr window)
    {
        if (window == IntPtr.Zero ||
            GetWindowThreadProcessId(window, out uint pid) == 0 || pid == 0)
            return false;
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            return process.ProcessName.StartsWith("MuMuNx", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}

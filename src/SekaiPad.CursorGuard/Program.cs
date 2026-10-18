using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SekaiPad;
using SekaiPad.CursorGuard;

internal static class Program
{
    private const uint SPI_SETCURSORS = 0x0057;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfo(
        uint action, uint param, IntPtr result, uint flags);

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch { /* A failed cleanup must not hide restoration failure. */ }
    }

    // A cursor marker is retained until SPI_SETCURSORS actually succeeded.
    // The previous implementation erased the marker even when Windows
    // rejected the API call, leaving no evidence of an invisible cursor.
    private static bool RestoreIfAbandoned(string marker, bool gesture)
    {
        if (!File.Exists(marker)) return true;
        if (gesture)
        {
            // Preserve the JSON snapshot if any of the Shell/COM restoration
            // steps fail. The owner can retry manually on an attended desktop.
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                try
                {
                    GestureSettings.RestoreFile(marker);
                    if (!File.Exists(marker)) return true;
                }
                catch { /* Retry transient Explorer/Shell failures. */ }
                if (attempt < 2) Thread.Sleep(600);
            }
            return false;
        }

        return CursorRestorePolicy.TryRestoreMarker(
            marker,
            () => SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0),
            maxAttempts: 3,
            beforeRetry: () => Thread.Sleep(250));
    }

    private static int Main(string[] args)
    {
        bool gesture = args.Length == 5 && args[0] == "--gesture";
        int offset = gesture ? 1 : 0;
        if (args.Length != offset + 4 ||
            !int.TryParse(args[offset], out int pid) ||
            !long.TryParse(args[offset + 1], out long expectedStart))
            return 2;

        string marker = args[offset + 2];
        string ready = args[offset + 3];
        bool parentMonitorError = false;
        bool recoverySucceeded = false;
        try
        {
            using var parent = Process.GetProcessById(pid);
            if (parent.StartTime.ToUniversalTime().Ticks == expectedStart)
            {
                File.WriteAllText(ready, "ready");
                parent.WaitForExit();
            }
            // A mismatched start time can indicate a recycled PID. Never
            // wait on or kill another user's process; just recover any marker.
        }
        catch (ArgumentException)
        {
            // Parent already exited before the watchdog attached.
        }
        catch (InvalidOperationException)
        {
            // Parent went away during startup. Recovery still runs below.
        }
        catch
        {
            parentMonitorError = true;
        }
        finally
        {
            try { recoverySucceeded = RestoreIfAbandoned(marker, gesture); }
            catch { recoverySucceeded = false; }
            TryDelete(ready);
        }

        // The marker is deliberately kept if a real recovery operation failed.
        // Returning success would make the crash guard look healthy when it
        // may have left all system cursors invisible or gestures suppressed.
        return !parentMonitorError && recoverySucceeded ? 0 : 1;
    }
}

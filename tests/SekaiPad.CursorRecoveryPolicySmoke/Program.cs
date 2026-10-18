using SekaiPad.CursorGuard;

internal static class Program
{
    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static int Main()
    {
        // Pure injected callbacks: NEVER run SystemParametersInfo,
        // mutate HKCU, install hooks, or attach to an active game.
        string directory = Path.Combine(Path.GetTempPath(),
            "SekaiPad-CursorPolicySmoke-" + Guid.NewGuid().ToString("N"));
        string marker = Path.Combine(directory, "synthetic-cursor.active");
        Directory.CreateDirectory(directory);
        try
        {
            int invocations = 0;
            bool nothingToRestore = CursorRestorePolicy.TryRestoreMarker(
                marker, () => { invocations++; return false; });
            Assert(nothingToRestore && invocations == 0,
                "Nonexistent marker unexpectedly invoked cursor restoration");
            Console.WriteLine("CURSOR_NO_MARKER_NO_SPI_PASS");

            File.WriteAllText(marker, "synthetic marker, no Windows cursor touched");
            invocations = 0;
            int delays = 0;
            bool failed = CursorRestorePolicy.TryRestoreMarker(
                marker, () => { invocations++; return false; },
                maxAttempts: 3, beforeRetry: () => delays++);
            Assert(!failed && File.Exists(marker) && invocations == 3 && delays == 2,
                "Failed reset must keep marker through all three retries");
            Console.WriteLine("CURSOR_FAILED_RESTORE_PRESERVES_MARKER_PASS");

            invocations = 0;
            delays = 0;
            bool eventuallySucceeded = CursorRestorePolicy.TryRestoreMarker(
                marker, () =>
                {
                    invocations++;
                    if (invocations == 1) throw new IOException("Synthetic transient error");
                    return invocations == 3;
                }, maxAttempts: 3, beforeRetry: () => delays++);
            Assert(eventuallySucceeded && !File.Exists(marker) &&
                   invocations == 3 && delays == 2,
                "Retry success must remove marker only after restoration succeeds");
            Console.WriteLine("CURSOR_SUCCESS_REMOVES_MARKER_AFTER_RETRY_PASS");

            File.WriteAllText(marker, "synthetic marker again");
            invocations = 0;
            bool immediate = CursorRestorePolicy.TryRestoreMarker(
                marker, () => { invocations++; return true; });
            Assert(immediate && invocations == 1 && !File.Exists(marker),
                "Immediate recovery must remove marker exactly once");
            Console.WriteLine("CURSOR_SUCCESS_ONE_CALL_PASS");

            bool badCountRejected = false;
            try
            {
                CursorRestorePolicy.TryRestoreMarker(marker, () => true, maxAttempts: 0);
            }
            catch (ArgumentOutOfRangeException) { badCountRejected = true; }
            Assert(badCountRejected, "Zero recovery attempts must fail closed");
            Console.WriteLine("CURSOR_INVALID_RETRY_COUNT_REJECTED_PASS");

            Assert(Directory.Exists(directory),
                "Test scratch directory unexpectedly deleted during operation");
            Console.WriteLine("CURSOR_RESTORE_POLICY_ALL_OFFLINE_CHECKS_PASS");
            return 0;
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}

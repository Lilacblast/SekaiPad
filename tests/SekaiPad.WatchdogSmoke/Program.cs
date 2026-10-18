using System.Diagnostics;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--dummy-parent")
        {
            // A harmless process to kill: never registers Windows input
            // hooks, changes desktop cursor images or writes to the registry.
            Thread.Sleep(TimeSpan.FromMinutes(2));
            return 0;
        }

        if (args.Length != 2 || args[0] != "--guard")
        {
            Console.Error.WriteLine("Usage: WatchdogSmoke --guard <CursorGuard.exe>");
            return 2;
        }

        string guardPath = Path.GetFullPath(args[1]);
        if (!File.Exists(guardPath) ||
            !Path.GetFileName(guardPath).Equals(
                "SekaiPad.CursorGuard.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Safety guard EXE absent", guardPath);

        string directory = Path.Combine(Path.GetTempPath(),
            "SekaiPad-WatchdogSmoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, "nonexistent-cursor-marker");
        string ready = marker + ".ready";

        static void Require(bool valid, string description)
        {
            if (!valid) throw new Exception(description);
        }

        Process? parent = null;
        Process? guard = null;
        try
        {
            Require(!File.Exists(marker) && !File.Exists(ready),
                "Temporary safety test markers must start absent");
            string selfPath = Path.Combine(AppContext.BaseDirectory, "SekaiPad.WatchdogSmoke.exe");
            Require(File.Exists(selfPath), "Self-contained test apphost EXE is missing");
            var parentInfo = new ProcessStartInfo(selfPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            parentInfo.ArgumentList.Add("--dummy-parent");
            parent = Process.Start(parentInfo) ??
                throw new Exception("Could not start harmless dummy parent");
            long startTicks = parent.StartTime.ToUniversalTime().Ticks;

            var guardInfo = new ProcessStartInfo(guardPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            guardInfo.ArgumentList.Add(parent.Id.ToString());
            guardInfo.ArgumentList.Add(startTicks.ToString());
            guardInfo.ArgumentList.Add(marker);
            guardInfo.ArgumentList.Add(ready);
            guard = Process.Start(guardInfo) ??
                throw new Exception("Could not start independent crash guard");

            bool acknowledged = SpinWait.SpinUntil(
                () => File.Exists(ready) || guard.HasExited,
                TimeSpan.FromSeconds(8));
            Require(acknowledged && !guard.HasExited && File.Exists(ready),
                "Crash guard did not acknowledge an actual parent process");

            // Test an invalid start time while the synthetic parent remains
            // alive. Distinct absent markers prevent affecting the real
            // guard instance started by this test.
            string mismatchMarker = Path.Combine(directory, "absent-identity-marker");
            string mismatchReady = mismatchMarker + ".ready";
            using var wrong = Process.Start(new ProcessStartInfo(guardPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    parent.Id.ToString(),
                    (startTicks - 1000000).ToString(),
                    mismatchMarker,
                    mismatchReady
                }
            }) ?? throw new Exception("Unable to launch start-time mismatch test");
            Require(wrong.WaitForExit(8000) && wrong.ExitCode == 0,
                "Crash guard should reject mismatched parent identity");
            Require(!File.Exists(mismatchReady) && !File.Exists(mismatchMarker),
                "Identity mismatch created input restoration markers");
            Console.WriteLine("CRASH_GUARD_PID_START_TIME_IDENTITY_PASS");

            // Kill ONLY the harmless child created above. Never kill a
            // real SekaiPad, browser, shell or user game.
            parent.Kill(entireProcessTree: true);
            Require(parent.WaitForExit(8000), "Synthetic parent failed to exit");
            Require(guard.WaitForExit(8000),
                "Independent crash guard did not notice parent death");
            Require(guard.ExitCode == 0, "Crash guard reported failure");
            Require(!File.Exists(ready) && !File.Exists(marker),
                "Guard did not clean its own acknowledgment marker");
            Console.WriteLine("CRASH_GUARD_DUMMY_PARENT_KILL_LIFECYCLE_PASS");
            Console.WriteLine("CRASH_GUARD_READY_AND_ACK_REMOVAL_PASS");

            // An invalid HKCU gesture snapshot must remain on disk and cause
            // a nonzero guard exit. Validation rejects it BEFORE registry
            // mutation; no real gesture settings or cursor images are changed.
            string invalidGesture = Path.Combine(directory, "invalid-gesture.json");
            string invalidReady = invalidGesture + ".ready";
            File.WriteAllText(invalidGesture, "{\"Values\":[]}");
            using (var failedGuard = Process.Start(new ProcessStartInfo(guardPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "--gesture",
                    Environment.ProcessId.ToString(),
                    "0", // intentionally not this process's actual start time
                    invalidGesture,
                    invalidReady
                }
            }) ?? throw new Exception("Unable to launch malformed-backup guard test"))
            {
                Require(failedGuard.WaitForExit(8000) && failedGuard.ExitCode != 0,
                    "Malformed gesture recovery must return an explicit failure");
            }
            Require(File.Exists(invalidGesture) && !File.Exists(invalidReady),
                "Failed gesture recovery must preserve its backup and clean readiness");
            Console.WriteLine("GESTURE_CORRUPT_BACKUP_GUARD_FAILS_CLOSED_PASS");
            Console.WriteLine("CRASH_GUARD_NO_WINDOWS_INPUT_CHANGES_PASS");
            return 0;
        }
        finally
        {
            if (parent is { HasExited: false })
            {
                try { parent.Kill(entireProcessTree: true); } catch { }
                try { parent.WaitForExit(2000); } catch { }
            }
            if (guard is { HasExited: false })
            {
                try { guard.Kill(entireProcessTree: true); } catch { }
                try { guard.WaitForExit(2000); } catch { }
            }
            parent?.Dispose();
            guard?.Dispose();
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}

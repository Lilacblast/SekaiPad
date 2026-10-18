using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using SekaiPad;

internal static class Program
{
    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
    }

    // Safety smoke is deliberately read-only with respect to Windows:
    // NO low-level hook, NO cursor mask, NO HKCU writes, NO simulator input.
    [STAThread]
    public static int Main()
    {
        var cacheType = typeof(GestureSettings).Assembly.GetType(
            "SekaiPad.ImmersiveTouchpadSettingsCache", throwOnError: true)!;
        var probe = cacheType.GetMethod("IsVerifiedShellBuild",
            BindingFlags.Public | BindingFlags.Static)!;
        bool Supported(int version) => (bool)probe.Invoke(null, [version])!;
        Require(!Supported(19045), "Windows 10 cache must be blocked");
        Require(!Supported(22621), "Windows 11 22H2 cache must be blocked");
        Require(!Supported(26100), "Windows 11 24H2 cache must be blocked");
        Require(Supported(26200), "Only tested Windows 11 25H2 build is accepted");
        Require(!Supported(26201), "Unverified newer build must fail closed");
        Console.WriteLine("GESTURE_VERIFIED_BUILD_ALLOWLIST_PASS 26200 only");

        // Prove the global-cursor guard does not confuse a stale/dead process
        // handle with a live independent recovery watchdog. No system cursor
        // is hidden or replaced by this read-only lifetime check.
        var cursorType = typeof(GestureSettings).Assembly.GetType(
            "SekaiPad.SystemCursorVisibility", throwOnError: true)!;
        var aliveMethod = cursorType.GetMethod("IsWatchdogAlive",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Alive(Process? process) =>
            (bool)aliveMethod.Invoke(null, [process])!;
        Require(!Alive(null), "Null cursor watchdog should not be considered alive");
        using (var dummy = Process.GetCurrentProcess())
        {
            Require(Alive(dummy), "Current process should be alive");
            dummy.Dispose();
            Require(!Alive(dummy),
                "Disposed watchdog process handle should not be considered alive");
        }
        Console.WriteLine("CURSOR_WATCHDOG_LIVENESS_FAIL_CLOSED_PASS");

        // On unverified Windows, exercise the *real* guard's preflight but
        // verify that it never creates backup markers or writes HKCU.
        // This test must never invoke the gesture suppression path on 26200.
        if (!Supported(Environment.OSVersion.Version.Build))
        {
            string Fingerprint()
            {
                using var settings = Registry.CurrentUser.OpenSubKey(
                    GestureSettings.RegistryPath);
                return settings == null
                    ? "(no PTP settings key)"
                    : string.Join("|", GestureSettings.Names.Select(name =>
                        settings.GetValueNames().Contains(name,
                            StringComparer.OrdinalIgnoreCase)
                            ? name + ":" + settings.GetValue(name) + ":" + settings.GetValueKind(name)
                            : name + ":absent"));
            }

            string before = Fingerprint();
            var systemGuard = new WindowsGestureGuard();
            var markerField = typeof(WindowsGestureGuard).GetField(
                "_backupPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
            string marker = (string)markerField.GetValue(systemGuard)!;
            Require(!File.Exists(marker), "Unexpected pre-existing guard marker");
            bool rejected = false;
            try { systemGuard.Enable(); }
            catch (NotSupportedException) { rejected = true; }
            Require(rejected, "Unsupported Windows unexpectedly enabled gestures");
            Require(!File.Exists(marker) && !File.Exists(marker + ".ready"),
                "Unsupported Windows armed a recovery marker");
            Require(Fingerprint() == before,
                "Unsupported Windows changed gesture settings before refusing input");
            Console.WriteLine("UNSUPPORTED_WINDOWS_NO_REGISTRY_WRITE_PASS");
        }

        string[] keys =
        [
            "ThreeFingerTapEnabled", "FourFingerTapEnabled",
            "ThreeFingerSlideEnabled", "FourFingerSlideEnabled"
        ];
        Require(GestureSettings.Names.SequenceEqual(keys),
            "Windows gesture registry isolation must cover exactly four keys");
        var original = new GestureSettingSnapshot
        {
            Values =
            [
                new() { Name = keys[0], Existed = false, Value = 0 },
                new() { Name = keys[1], Existed = true, Value = 1 },
                new() { Name = keys[2], Existed = true, Value = 2 },
                new() { Name = keys[3], Existed = false, Value = 0 }
            ]
        };
        // All malformed backups are rejected before any HKCU writes. These
        // tests only construct in-memory data; they do not call Restore().
        GestureSettings.ValidateSnapshot(original);
        static GestureSettingSnapshot Copy(GestureSettingSnapshot snapshot) =>
            JsonSerializer.Deserialize<GestureSettingSnapshot>(
                JsonSerializer.Serialize(snapshot))!;
        static void RequireRejected(GestureSettingSnapshot? snapshot, string label)
        {
            bool refused = false;
            try { GestureSettings.ValidateSnapshot(snapshot); }
            catch (InvalidDataException) { refused = true; }
            Require(refused, "Malformed recovery snapshot was accepted: " + label);
        }
        RequireRejected(null, "null snapshot");
        RequireRejected(new GestureSettingSnapshot(), "empty snapshot");
        var incomplete = Copy(original);
        incomplete.Values = incomplete.Values[..3];
        RequireRejected(incomplete, "three entries only");
        var extra = Copy(original);
        extra.Values = [..extra.Values, new GestureSettingValue
            { Name = keys[0], Existed = false }];
        RequireRejected(extra, "five entries");
        var duplicated = Copy(original);
        duplicated.Values[1].Name = duplicated.Values[0].Name;
        RequireRejected(duplicated, "duplicate setting name");
        var unknown = Copy(original);
        unknown.Values[2].Name = "InventedGestureSetting";
        RequireRejected(unknown, "unknown setting name");
        var wrongCase = Copy(original);
        wrongCase.Values[2].Name = keys[2].ToLowerInvariant();
        RequireRejected(wrongCase, "noncanonical setting name");
        var absentNonzero = Copy(original);
        absentNonzero.Values[0].Value = 1;
        RequireRejected(absentNonzero, "nonzero value for absent setting");
        var nullEntry = Copy(original);
        nullEntry.Values[1] = null!;
        RequireRejected(nullEntry, "null entry");
        var restoredOrder = Copy(original);
        Array.Reverse(restoredOrder.Values);
        GestureSettings.ValidateSnapshot(restoredOrder);
        Console.WriteLine("GESTURE_MALFORMED_BACKUP_REJECTION_PASS 8 invalid cases");
        Console.WriteLine("GESTURE_BACKUP_ENTRY_ORDER_INDEPENDENCE_PASS");

        string root = Path.Combine(Path.GetTempPath(),
            "SekaiPad-SafetySmoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "gesture-backup.json");
            GestureSettings.Save(path, original);
            Require(File.Exists(path), "Crash recovery backup was not persisted");
            var decoded = JsonSerializer.Deserialize<GestureSettingSnapshot>(
                File.ReadAllText(path))!;
            Require(decoded.Values.Length == 4, "Incorrect backup entry count");
            for (int i = 0; i < 4; ++i)
            {
                var old = original.Values[i];
                var read = decoded.Values[i];
                Require(read.Name == old.Name && read.Existed == old.Existed &&
                        read.Value == old.Value,
                    "Crash recovery JSON loses present/absent or original value for " + old.Name);
            }
            Console.WriteLine("GESTURE_BACKUP_ROUNDTRIP_PASS key-presence and original values");
            Require(!File.Exists(path + ".tmp"),
                "Partial write temporary file left by successful backup");
            Console.WriteLine("GESTURE_BACKUP_ATOMIC_WRITE_PASS");
            var badPath = Path.Combine(root, "invalid-backup.json");
            bool badSaveRejected = false;
            try { GestureSettings.Save(badPath, incomplete); }
            catch (InvalidDataException) { badSaveRejected = true; }
            Require(badSaveRejected && !File.Exists(badPath),
                "Invalid backup must never be written for crash recovery");
            Console.WriteLine("GESTURE_INVALID_BACKUP_NOT_PERSISTED_PASS");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        // CI Windows Server has no PTP device/HKCU gesture key.
        // On the one verified desktop, perform a *read-only* comparison.
        // Never call Suppress/Restore or install any global hooks here.
        if (Environment.OSVersion.Version.Build == 26200)
        {
            var system = GestureSettings.Capture();
            Require(system.Values.Length == 4,
                "Windows PTP user settings are absent/unreadable");
            var saved = system.Values.Select(v =>
                new GestureSettingValue { Name = v.Name, Existed = v.Existed, Value = v.Value })
                .ToArray();
            var again = GestureSettings.Capture();
            Require(saved.Zip(again.Values).All(pair =>
                    pair.First.Name == pair.Second.Name &&
                    pair.First.Existed == pair.Second.Existed &&
                    pair.First.Value == pair.Second.Value),
                "Windows settings mutated unexpectedly during read-only validation");
            Console.WriteLine("GESTURE_SETTINGS_READ_ONLY_PASS");
        }
        else
            Console.WriteLine("GESTURE_REGISTRY_SNAPSHOT_SKIPPED no verified PTP desktop");
        Console.WriteLine("ALL_NONINTERACTIVE_SAFETY_CHECKS_PASS");
        return 0;
    }
}

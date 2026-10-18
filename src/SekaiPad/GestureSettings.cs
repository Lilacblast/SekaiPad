using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace SekaiPad;

// The Settings UI writes these HKCU values. Windows 11 Explorer caches them;
// writing the registry alone does not stop gestures until the shell refreshes.
public sealed class GestureSettingSnapshot
{
    public GestureSettingValue[] Values { get; set; } = [];
}

public sealed class GestureSettingValue
{
    public string Name { get; set; } = "";
    public bool Existed { get; set; }
    public int Value { get; set; }
}

public static class GestureSettings
{
    public const string RegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

    public static readonly string[] Names =
    [
        "ThreeFingerTapEnabled", "FourFingerTapEnabled",
        "ThreeFingerSlideEnabled", "FourFingerSlideEnabled"
    ];

    private static readonly int[] CacheIds = [0x0f, 0x10, 0x11, 0x12];

    // A corrupt or incomplete backup must be rejected in its entirety BEFORE
    // touching even one HKCU value. RestoreFile intentionally retains it for
    // diagnosis rather than risking a partial restore of three/four-finger
    // gesture settings. A valid backup preserves whether a value was absent.
    public static void ValidateSnapshot(GestureSettingSnapshot? snapshot)
    {
        if (snapshot?.Values == null || snapshot.Values.Length != Names.Length)
            throw new InvalidDataException(
                "Touchpad recovery snapshot must contain exactly four settings.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in snapshot.Values)
        {
            if (item == null || !Names.Contains(item.Name, StringComparer.Ordinal) ||
                !seen.Add(item.Name))
                throw new InvalidDataException(
                    "Touchpad recovery snapshot contains unknown or duplicate setting.");
            if (!item.Existed && item.Value != 0)
                throw new InvalidDataException(
                    "Touchpad recovery snapshot has nonzero value for an absent setting.");
        }
    }

    public static GestureSettingSnapshot Capture()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        if (key == null)
            throw new IOException("Windows Precision Touchpad settings are not available.");

        var values = new List<GestureSettingValue>();
        foreach (string name in Names)
        {
            bool existed = key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase);
            if (existed && key.GetValueKind(name) != RegistryValueKind.DWord)
                throw new IOException("Unexpected touchpad setting registry type for " + name);
            values.Add(new GestureSettingValue
            {
                Name = name,
                Existed = existed,
                Value = existed ? (int)key.GetValue(name)! : 0
            });
        }
        return new GestureSettingSnapshot { Values = values.ToArray() };
    }

    private static void SetValues(GestureSettingSnapshot? previous, bool suppress)
    {
        // Validate all saved entries before the first registry write.
        if (!suppress) ValidateSnapshot(previous);
        // SAFETY: The undocumented Shell cache must be proven available
        // *before* any disabling write. Previous code zeroed HKCU first
        // and could fail to restore on an unsupported Windows build.
        using var verifiedCache = suppress ? new ImmersiveTouchpadSettingsCache() : null;
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true)
            ?? throw new IOException("Cannot open Windows Precision Touchpad settings for writing.");

        foreach (string name in Names)
        {
            if (suppress)
            {
                key.SetValue(name, 0, RegistryValueKind.DWord);
                continue;
            }
            var original = previous?.Values.SingleOrDefault(v => v.Name == name)
                ?? throw new InvalidDataException("Missing gesture setting backup: " + name);
            if (original.Existed)
                key.SetValue(name, original.Value, RegistryValueKind.DWord);
            else
                key.DeleteValue(name, throwOnMissingValue: false);
        }
        key.Flush();
        if (suppress)
        {
            verifiedCache!.NotifyChanges(CacheIds);
            for (int i = 0; i < CacheIds.Length; ++i)
                if (verifiedCache.ReadDWORD(CacheIds[i]) != 0)
                    throw new IOException("Explorer retained its previous touchpad gesture setting: " + Names[i]);
        }
        else
        {
            // Restore HKCU first even if Windows was updated or Explorer
            // no longer exposes the private API. Retain the on-disk
            // snapshot if live cache refresh is unavailable.
            using var cache = new ImmersiveTouchpadSettingsCache();
            cache.NotifyChanges(CacheIds);
        }
    }

    public static void Suppress() => SetValues(null, suppress: true);
    public static void Restore(GestureSettingSnapshot snapshot) =>
        SetValues(snapshot, suppress: false);

    public static void Save(string path, GestureSettingSnapshot snapshot)
    {
        ValidateSnapshot(snapshot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
        File.Move(temp, path);
    }

    public static void RestoreFile(string path)
    {
        if (!File.Exists(path)) return;
        var snapshot = JsonSerializer.Deserialize<GestureSettingSnapshot>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid Windows gesture snapshot");
        ValidateSnapshot(snapshot);
        Restore(snapshot);
        File.Delete(path);
    }
}

internal sealed class ImmersiveTouchpadSettingsCache : IDisposable
{
    private IntPtr _provider;
    private IntPtr _cache;
    private static readonly Guid ImmersiveShell =
        new("c2f03a33-21f5-47fa-b4bb-156362a2f239");
    private static readonly Guid IServiceProvider =
        new("6d5140c1-7436-11ce-8034-00aa006009fa");
    private static readonly Guid CacheService =
        new("53660488-8855-460b-a9ab-5cfc6b5012ca");
    private static readonly Guid CacheInterface =
        new("4214f6fa-eb36-4e2f-9ca2-23fdc1832df7");

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryService(IntPtr self, ref Guid service, ref Guid iid, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SettingChanged(IntPtr self, int setting);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDword(IntPtr self, int setting, out uint result);

    private static T Method<T>(IntPtr instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(
            Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));

    public static bool IsVerifiedShellBuild(int build) => build == 26200;

    public ImmersiveTouchpadSettingsCache()
    {
        // This undocumented Shell/COM vtable layout has ONLY been validated
        // on Windows 11 25H2 build 26200. Fail closed on EVERY other build.
        int build = Environment.OSVersion.Version.Build;
        if (!IsVerifiedShellBuild(build))
            throw new NotSupportedException(
                $"Windows touchpad cache layout is unverified on build {build}.");
        Guid clsid = ImmersiveShell, providerId = IServiceProvider;
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 0x17, ref providerId, out _provider);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
        try
        {
            Guid service = CacheService, iid = CacheInterface;
            hr = Method<QueryService>(_provider, 3)(_provider, ref service, ref iid, out _cache);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void NotifyChanges(IEnumerable<int> settings)
    {
        foreach (int setting in settings)
        {
            int hr = Method<SettingChanged>(_cache, 3)(_cache, setting);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }
    }

    public uint ReadDWORD(int setting)
    {
        int hr = Method<GetDword>(_cache, 5)(_cache, setting, out uint value);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return value;
    }

    public void Dispose()
    {
        if (_cache != IntPtr.Zero)
        {
            Marshal.Release(_cache);
            _cache = IntPtr.Zero;
        }
        if (_provider != IntPtr.Zero)
        {
            Marshal.Release(_provider);
            _provider = IntPtr.Zero;
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SekaiPad;

public interface IEmulatorTouchMarkers
{
    bool Active { get; }
    Task EnableAsync();
    Task RestoreAsync();
}

public sealed class MuMuTouchMarkers : IEmulatorTouchMarkers
{
    private const string Serial = "127.0.0.1:5555";
    private static readonly string Adb = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "MuMuPlayer", "nx_main", "adb.exe");

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private string? _originalValue;
    private bool _active;

    public bool Active => _active;

    private static async Task<string> RunAsync(params string[] args)
    {
        if (!File.Exists(Adb))
            throw new FileNotFoundException("MuMu ADB not found.", Adb);
        var info = new ProcessStartInfo(Adb)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-s");
        info.ArgumentList.Add(Serial);
        foreach (string arg in args)
            info.ArgumentList.Add(arg);

        using var process = Process.Start(info) ?? throw new IOException("ADB did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("MuMu show-touches ADB command timed out.");
        }
        string result = (await output + await errors).Trim();
        if (process.ExitCode != 0)
            throw new IOException("MuMu ADB failed: " + result);
        return result;
    }

    public async Task EnableAsync()
    {
        await _mutex.WaitAsync();
        try
        {
            if (_active) return;
            _originalValue = (await RunAsync("shell", "settings",
                "get", "system", "show_touches")).Trim();
            if (_originalValue != "1")
                await RunAsync("shell", "settings", "put", "system", "show_touches", "1");
            var check = await RunAsync("shell", "settings", "get", "system", "show_touches");
            if (check != "1")
                throw new IOException("Android did not enable Show taps.");
            _active = true;
        }
        catch
        {
            try { await RestoreCoreAsync(); } catch { /* Keep original for retry. */ }
            throw;
        }
        finally { _mutex.Release(); }
    }

    private async Task RestoreCoreAsync()
    {
        if (_originalValue == null) return;
        if (_originalValue == "null")
            await RunAsync("shell", "settings", "delete", "system", "show_touches");
        else
            await RunAsync("shell", "settings", "put", "system",
                "show_touches", _originalValue);
        _originalValue = null;
        _active = false;
    }

    public async Task RestoreAsync()
    {
        await _mutex.WaitAsync();
        try { await RestoreCoreAsync(); }
        finally { _mutex.Release(); }
    }
}

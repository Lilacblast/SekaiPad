using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using Android.Emulation.Control;

namespace SekaiPad;

/// <summary>
/// Windows Precision Touchpad -> scrcpy v5.0 control-only server on a local MuMu
/// Android 15 guest. Uses ADB REVERSE: a loopback TCP listener on Windows.
/// This is deliberate: ADB FORWARD was repeatedly reset on the same guest.
/// The server runs independently from the game's binaries.
/// </summary>
public sealed class ScrcpyTouchBridge : ITouchBridge
{
    private const string Serial = "127.0.0.1:5555";
    private const string RemoteServer = "/data/local/tmp/sekaipad-scrcpy5-server.jar";
    private const ushort ScreenWidth = 1920;
    private const ushort ScreenHeight = 1080;

    private readonly Channel<ScrcpyQueuedFrame> _queue =
        Channel.CreateBounded<ScrcpyQueuedFrame>(new BoundedChannelOptions(128)
        {
            SingleReader = true,
            SingleWriter = false, // Send() and asynchronous fault completion may race.
            FullMode = BoundedChannelFullMode.Wait
        });

    private readonly ScrcpyTouchPackets _codec = new();
    private TcpListener? _listener;
    private TcpClient? _client;
    private Process? _server;
    private Task? _writerTask;
    private string? _abstractSocket;
    private bool _connected;
    private bool _disposing;
    private int _discardPending; // 1 means release live pointers and skip stale queued frames.
    private long _framesQueued;
    private long _framesPumped;
    private long _packetsWritten;
    private long _lastPacketTicksUtc;
    private long _lastLocalQueueAgeUs;
    private long _peakLocalQueueAgeUs;

    public long FramesQueued => Interlocked.Read(ref _framesQueued);
    public long FramesPumped => Interlocked.Read(ref _framesPumped);
    public long PacketsWritten => Interlocked.Read(ref _packetsWritten);
    public int PendingFrames => _queue.Reader.CanCount ? _queue.Reader.Count : 0;
    // Monotonic enqueue-to-host-pump timing; never a game-judgment benchmark.
    public long LastLocalQueueAgeUs => Interlocked.Read(ref _lastLocalQueueAgeUs);
    public long PeakLocalQueueAgeUs => Interlocked.Read(ref _peakLocalQueueAgeUs);
    public DateTime? LastPacketUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastPacketTicksUtc);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    private static readonly string Home = System.Environment.GetFolderPath(
        System.Environment.SpecialFolder.UserProfile);
    private static readonly string AdbPath = Path.Combine(
        Home, "MuMuPlayer", "nx_main", "adb.exe");
    // Public preview: no developer private-workspace fallbacks.
    // Official scrcpy-server must be staged separately, never bundled.
    private static readonly string LocalServer = FindScrcpyServer();

    private static string FindScrcpyServer()
    {
        string portable = Path.Combine(AppContext.BaseDirectory, "tools", "scrcpy-server");
        string userLocal = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "SekaiPad", "tools", "scrcpy-server");
        return File.Exists(portable) ? portable : userLocal;
    }

    public event Action<Exception>? Faulted;
    public bool Connected => _connected;

    public async Task ConnectAsync(string endpoint)
    {
        if (_connected) throw new InvalidOperationException("Already connected.");
        if (endpoint.Trim() is not (Serial or "localhost:5555"))
            throw new InvalidOperationException("MuMu accepts only the local ADB serial 127.0.0.1:5555.");
        if (!File.Exists(AdbPath) || !File.Exists(LocalServer))
            throw new FileNotFoundException("MuMu ADB or scrcpy 5.0 server not installed.");

        try
        {
            await AdbAsync(10, "connect", Serial);
            var android = (await AdbAsync(10, "-s", Serial, "shell",
                "getprop", "ro.build.version.release")).Trim();
            if (android != "15")
                throw new InvalidOperationException("Expected isolated MuMu Android 15; got " + android);

            // Verify the game package is present, without touching its account or gameplay.
            var package = await AdbAsync(10, "-s", Serial, "shell",
                "pm", "path", "com.sega.pjsekai");
            if (!package.Contains("package:", StringComparison.Ordinal))
                throw new InvalidOperationException("Japanese PJSK installation not found on MuMu.");

            await AdbAsync(35, "-s", Serial, "push", LocalServer, RemoteServer);

            int scid = RandomNumberGenerator.GetInt32(1, int.MaxValue);
            _abstractSocket = $"localabstract:scrcpy_{scid:x8}";
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1);
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            await AdbAsync(10, "-s", Serial, "reverse", _abstractSocket, $"tcp:{port}");

            var args = new[]
            {
                "-s", Serial, "shell", $"CLASSPATH={RemoteServer}", "app_process", "/",
                "com.genymobile.scrcpy.Server", "5.0",
                $"scid={scid:x8}", "log_level=info",
                "video=false", "audio=false", "control=true",
                "tunnel_forward=false", "send_dummy_byte=false",
                "send_device_meta=false", "clipboard_autosync=false",
                "cleanup=false", "stay_awake=false"
            };
            _server = StartAdb(args);
            _server.EnableRaisingEvents = true;
            _server.Exited += (_, _) =>
            {
                if (!_disposing && _connected)
                    ReportFault(new IOException("MuMu scrcpy-server disconnected."));
            };
            // Drain both pipes to prevent process deadlocks.
            _server.BeginOutputReadLine();
            _server.BeginErrorReadLine();

            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _client = await _listener.AcceptTcpClientAsync(limit.Token);
            _client.NoDelay = true;
            _listener.Stop();
            _listener = null;
            if (_server.HasExited)
                throw new IOException("scrcpy-server exited while establishing control.");
            _connected = true;
            _writerTask = Task.Run(PumpAsync);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public bool Send(TouchEvent frame)
    {
        if (!_connected || _disposing) return false;
        if (_queue.Writer.TryWrite(new ScrcpyQueuedFrame(frame, Stopwatch.GetTimestamp())))
        {
            Interlocked.Increment(ref _framesQueued);
            return true;
        }
        // Fail closed without replaying 128 stale note frames. The single
        // reader skips its backlog and emits UP only for fingers it sent.
        // Do not close TCP here: the reader must get a chance to release.
        Volatile.Write(ref _discardPending, 1);
        _connected = false;
        _queue.Writer.TryComplete();
        Faulted?.Invoke(new IOException(
            "MuMu input queue saturated; sending emergency finger releases."));
        return false;
    }

    private async Task PumpAsync()
    {
        try
        {
            if (_client == null) return;
            await ScrcpyFramePump.RunAsync(
                _client.GetStream(), _queue.Reader, _codec, ScreenWidth, ScreenHeight,
                () => Volatile.Read(ref _discardPending) != 0,
                count =>
                {
                    Interlocked.Add(ref _packetsWritten, count);
                    Interlocked.Exchange(ref _lastPacketTicksUtc, DateTime.UtcNow.Ticks);
                },
                ageUs =>
                {
                    Interlocked.Increment(ref _framesPumped);
                    Interlocked.Exchange(ref _lastLocalQueueAgeUs, ageUs);
                    long peak;
                    do
                    {
                        peak = Interlocked.Read(ref _peakLocalQueueAgeUs);
                        if (ageUs <= peak) break;
                    } while (Interlocked.CompareExchange(
                        ref _peakLocalQueueAgeUs, ageUs, peak) != peak);
                });
        }
        catch (Exception ex)
        {
            if (!_disposing) ReportFault(ex);
        }
    }

    private void ReportFault(Exception ex)
    {
        _connected = false;
        _queue.Writer.TryComplete(ex);
        try { _client?.Close(); } catch { }
        Faulted?.Invoke(ex);
    }

    private static Process StartAdb(IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(AdbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new IOException("Unable to start local ADB");
    }

    private static async Task<string> AdbAsync(int timeoutSeconds, params string[] args)
    {
        using var proc = StartAdb(args);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var output = proc.StandardOutput.ReadToEndAsync(cts.Token);
        var errors = proc.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            throw new TimeoutException("MuMu ADB command timed out.");
        }
        string result = await output + await errors;
        if (proc.ExitCode != 0)
            throw new IOException("MuMu ADB returned " + proc.ExitCode + ": " + result);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposing) return;
        _disposing = true;
        _connected = false;
        // Normal disconnect/exit also prioritizes UP over queued late notes.
        Volatile.Write(ref _discardPending, 1);
        _queue.Writer.TryComplete();
        if (_writerTask != null)
            await Task.WhenAny(_writerTask, Task.Delay(1250));
        _client?.Dispose();
        _listener?.Stop();
        if (_server is { HasExited: false })
        {
            try
            {
                await Task.WhenAny(_server.WaitForExitAsync(), Task.Delay(750));
                if (!_server.HasExited) _server.Kill(entireProcessTree: true);
            }
            catch { /* The emulator may already have exited. */ }
        }
        _server?.Dispose();
        if (_abstractSocket != null)
        {
            try { await AdbAsync(3, "-s", Serial, "reverse", "--remove", _abstractSocket); }
            catch { /* A stopped emulator has no active reverse mapping. */ }
            _abstractSocket = null;
        }
    }
}

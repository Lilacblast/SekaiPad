using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Android.Emulation.Control;
using SekaiPad;

/// <summary>TCP byte-stream and backlog-abort tests with no emulator/ADB.</summary>
internal static class FramePumpTests
{
    private sealed class CountingStream : MemoryStream
    {
        public int Writes { get; private set; }
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ++Writes;
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class TripAfterFirstWrite : MemoryStream
    {
        public Action? Trip { get; init; }
        public int Writes { get; private set; }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++Writes == 1) Trip?.Invoke();
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    private static TouchEvent Frame(params (int Id, int X, int Y, int Pressure)[] points)
    {
        var frame = new TouchEvent { Display = 0 };
        foreach (var (id, x, y, pressure) in points)
            frame.Touches.Add(new Touch
            {
                Identifier = id, X = x, Y = y, Pressure = pressure
            });
        return frame;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static async Task RunLoopbackAsync()
    {
        // Isolated localhost transport: no Android, ADB, account or GUI state.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        try
        {
            var accept = listener.AcceptTcpClientAsync(timeout.Token);
            using var sender = new TcpClient { NoDelay = true };
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await sender.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            using var receiver = await accept;
            var queue = Channel.CreateUnbounded<ScrcpyQueuedFrame>();
            var down = Frame((17, 200, 500, 80), (18, 500, 500, 80),
                (19, 800, 500, 80), (20, 1100, 500, 80),
                (21, 1400, 500, 80));
            var up = Frame((17, 200, 500, 0), (18, 500, 500, 0),
                (19, 800, 500, 0), (20, 1100, 500, 0),
                (21, 1400, 500, 0));
            Require(queue.Writer.TryWrite(new ScrcpyQueuedFrame(
                down, Stopwatch.GetTimestamp())), "loopback DOWN enqueue");
            Require(queue.Writer.TryWrite(new ScrcpyQueuedFrame(
                up, Stopwatch.GetTimestamp())), "loopback UP enqueue");
            queue.Writer.TryComplete();
            int written = 0;
            int pumped = 0;
            await ScrcpyFramePump.RunAsync(
                sender.GetStream(), queue.Reader, new ScrcpyTouchPackets(),
                1920, 1080, () => false, count => written += count, _ => ++pumped);
            var received = new byte[10 * 32];
            await receiver.GetStream().ReadExactlyAsync(received, timeout.Token);
            var check = new ScrcpyTouchPackets();
            var expected = new List<byte>();
            foreach (var packet in check.Translate(down, 1920, 1080))
                expected.AddRange(packet);
            foreach (var packet in check.Translate(up, 1920, 1080))
                expected.AddRange(packet);
            Require(received.SequenceEqual(expected) && written == 10 &&
                    pumped == 2 && check.ActiveCount == 0,
                "real loopback TCP changed multi-contact packet stream");
            Console.WriteLine("SCRCPY_LOOPBACK_TCP_PASS five_finger_down_up=10 packets bytes=320");
        }
        finally
        {
            listener.Stop();
        }
    }

    public static async Task RunAsync()
    {
        var empty = ScrcpyTouchPackets.Batch(Array.Empty<byte[]>());
        Require(empty.Length == 0, "zero-packet batch should be empty");
        byte[] single = ScrcpyTouchPackets.Pack(0, 3, 9, 9, 1920, 1080);
        Require(ReferenceEquals(single, ScrcpyTouchPackets.Batch(new[] { single })),
            "one packet requires no copy");
        bool badLengthRejected = false;
        try { ScrcpyTouchPackets.Batch(new[] { new byte[31] }); }
        catch (ArgumentException) { badLengthRejected = true; }
        Require(badLengthRejected, "malformed packet length was accepted");

        var queue = Channel.CreateUnbounded<ScrcpyQueuedFrame>();
        var down = Frame((1, 320, 500, 80), (2, 1420, 500, 80));
        var same = Frame((1, 320, 500, 80), (2, 1420, 500, 80));
        var move = Frame((1, 330, 490, 80), (2, 1420, 500, 80));
        var up = Frame((1, 330, 490, 0), (2, 1420, 500, 0));
        var frames = new[] { down, same, move, up };
        foreach (var frame in frames)
            Require(queue.Writer.TryWrite(new ScrcpyQueuedFrame(frame, Stopwatch.GetTimestamp())),
                "queue input");
        queue.Writer.TryComplete();
        var stream = new CountingStream();
        var codec = new ScrcpyTouchPackets();
        int wrote = 0;
        int pumped = 0;
        long latestDelayUs = -1;
        await ScrcpyFramePump.RunAsync(stream, queue.Reader, codec, 1920, 1080,
            () => false, count => wrote += count,
            delayUs => { ++pumped; latestDelayUs = delayUs; });
        var reference = new ScrcpyTouchPackets();
        var expected = new List<byte>();
        foreach (var frame in frames)
            foreach (var packet in reference.Translate(frame, 1920, 1080))
                expected.AddRange(packet);
        Require(stream.ToArray().SequenceEqual(expected),
            "coalesced TCP stream changed packet bytes/order");
        Require(latestDelayUs >= 0 && latestDelayUs < 1_000_000,
            "monotonic local queue delay metric is invalid");
        Require(wrote == 5 && pumped == 4 && stream.Writes == 3,
            "one socket write per nonempty frame was not preserved");
        Require(codec.ActiveCount == 0, "normal replay left held fingers");
        Console.WriteLine("SCRCPY_FRAME_BATCH_PASS identical_bytes=160 writes=3 packets=5");

        // Seed the codec with an already written DOWN, then simulate a
        // saturated queue of pending old notes. The pump must send only UP.
        var aborted = Channel.CreateUnbounded<ScrcpyQueuedFrame>();
        for (int i = 0; i < 48; ++i)
            Require(aborted.Writer.TryWrite(new ScrcpyQueuedFrame(
                Frame((3, 50 + i, 160, 80)), Stopwatch.GetTimestamp())),
                "saturation fixture queue");
        aborted.Writer.TryComplete();
        var seededCodec = new ScrcpyTouchPackets();
        var seededDown = seededCodec.Translate(Frame((7, 555, 555, 80)), 1920, 1080);
        var abortStream = new CountingStream();
        abortStream.Write(ScrcpyTouchPackets.Batch(seededDown));
        int discardedFrameWrites = 0;
        int emergencyPackets = 0;
        await ScrcpyFramePump.RunAsync(abortStream, aborted.Reader, seededCodec,
            1920, 1080, () => true,
            count => emergencyPackets += count,
            _ => ++discardedFrameWrites);
        var raw = abortStream.ToArray();
        Require(raw.Length == 64 && raw[1] == 0 && raw[33] == 1,
            "stale backlog replayed, or emergency UP omitted");
        Require(discardedFrameWrites == 0 && emergencyPackets == 1 &&
                abortStream.Writes == 1 && seededCodec.ActiveCount == 0,
            "overload should release emitted contact with no stale notes");
        Console.WriteLine("SCRCPY_BACKLOG_ABORT_PASS stale_frames_discarded=48 emergency_up=1");

        // Simulate disposal/overflow arriving while the first multi-finger
        // write is being processed. The next queued note must not be replayed.
        var midwrite = Channel.CreateUnbounded<ScrcpyQueuedFrame>();
        foreach (var item in new[]
        {
            Frame((35, 200, 500, 80)),
            Frame((35, 260, 500, 80)),
            Frame((35, 260, 500, 0))
        })
            Require(midwrite.Writer.TryWrite(new ScrcpyQueuedFrame(
                item, Stopwatch.GetTimestamp())), "midwrite queue");
        midwrite.Writer.TryComplete();
        bool abortDuringWrite = false;
        var tripStream = new TripAfterFirstWrite { Trip = () => abortDuringWrite = true };
        int midwritePackets = 0;
        int midwriteFrames = 0;
        await ScrcpyFramePump.RunAsync(
            tripStream, midwrite.Reader, new ScrcpyTouchPackets(),
            1920, 1080, () => abortDuringWrite,
            count => midwritePackets += count, _ => ++midwriteFrames);
        byte[] abortResult = tripStream.ToArray();
        Require(abortResult.Length == 64 && abortResult[1] == 0 &&
                abortResult[33] == 1 && midwriteFrames == 1 &&
                midwritePackets == 2 && tripStream.Writes == 2,
            "mid-write session abort replayed stale notes or omitted UP");
        Console.WriteLine("SCRCPY_MIDWRITE_ABORT_PASS stale_frames_discarded=2 emergency_up=1");

        var graceful = Channel.CreateUnbounded<ScrcpyQueuedFrame>();
        Require(graceful.Writer.TryWrite(new ScrcpyQueuedFrame(
            Frame((9, 500, 600, 80)), Stopwatch.GetTimestamp())), "graceful queue");
        graceful.Writer.TryComplete();
        var gracefulStream = new CountingStream();
        var gracefulCodec = new ScrcpyTouchPackets();
        int gracefulPackets = 0;
        await ScrcpyFramePump.RunAsync(gracefulStream, graceful.Reader, gracefulCodec,
            1920, 1080, () => false, count => gracefulPackets += count, _ => {});
        var data = gracefulStream.ToArray();
        Require(data.Length == 64 && data[1] == 0 && data[33] == 1 &&
                gracefulPackets == 2 && gracefulStream.Writes == 2 &&
                gracefulCodec.ActiveCount == 0,
            "graceful shutdown must automatically release active pointers");
        Console.WriteLine("SCRCPY_AUTOMATIC_RELEASE_PASS down=1 up=1");

        // Bounded queue saturation is exercised in a disconnected synthetic
        // instance. No ADB/TCP socket, emulator, hotkey, or real HID is used.
        await using var isolatedBridge = new ScrcpyTouchBridge();
        Require(!isolatedBridge.Send(Frame((8, 40, 100, 80))),
            "disconnected bridge should reject input");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var connectedField = typeof(ScrcpyTouchBridge).GetField("_connected", flags)
            ?? throw new Exception("Missing connected-state field");
        var abortField = typeof(ScrcpyTouchBridge).GetField("_discardPending", flags)
            ?? throw new Exception("Missing backpressure abort field");
        connectedField.SetValue(isolatedBridge, true);
        int faults = 0;
        isolatedBridge.Faulted += _ => ++faults;
        for (int i = 0; i < 128; ++i)
            Require(isolatedBridge.Send(Frame((i, 50, 100, 80))),
                "bounded synthetic queue rejected an in-capacity frame");
        Require(!isolatedBridge.Send(Frame((1000, 50, 100, 80))),
            "bounded queue must refuse stale note overflow");
        Require(!isolatedBridge.Connected && isolatedBridge.FramesQueued == 128 &&
                isolatedBridge.PendingFrames == 128 && faults == 1 &&
                (int)abortField.GetValue(isolatedBridge)! == 1,
            "overflow did not enable fail-closed queued-frame abort");
        Require(!isolatedBridge.Send(Frame((1001, 50, 100, 80))) && faults == 1,
            "overflow must not emit repeated fault events");
        Console.WriteLine("SCRCPY_QUEUE_SATURATION_PASS capacity=128 abort=1 faults=1");
        await RunLoopbackAsync();
    }
}

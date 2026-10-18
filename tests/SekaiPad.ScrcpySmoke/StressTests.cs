using System.Diagnostics;
using Android.Emulation.Control;
using RawInput.Touchpad;
using SekaiPad;

/// <summary>Deterministic software-only regression; no ADB or real input.</summary>
internal static class StressTests
{
    public static void Run()
    {
        const int frames = 30_000;
        var random = new Random(20261015);
        var map = new TouchMapping { ScreenWidth = 1920, ScreenHeight = 1080 };
        var packets = new ScrcpyTouchPackets();
        int encoded = 0;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();

        for (int i = 0; i < frames; ++i)
        {
            int fingers = i % 179 < 110 ? 5 : i % 179 < 148 ? 2 : 0;
            var contacts = new TouchpadContact[fingers];
            for (int id = 0; id < fingers; ++id)
            {
                int x = Math.Clamp(200 + id * 720 + i % 29 + random.Next(-2, 3),
                                   0, 4010);
                int y = Math.Clamp(1600 - (i % 20) * 17 + random.Next(-4, 5),
                                   0, 2468);
                contacts[id] = new TouchpadContact(id + 1, x, y);
            }

            TouchEvent? frame = map.Update(contacts, 0, 4010, 0, 2468);
            if (frame != null)
            {
                var next = packets.Translate(frame, 1920, 1080);
                encoded += next.Count;
                if (next.Any(p => p.Length != 32 || p[0] != 2 ||
                                  p[1] is not (0 or 1 or 2)))
                    throw new Exception("Malformed scrcpy v5.0 packet");
            }

            if (map.ActiveCount != fingers || packets.ActiveCount != fingers)
                throw new Exception($"Frame {i} pointer count mismatch");
        }

        var release = map.ReleaseAll();
        if (release != null)
            encoded += packets.Translate(release, 1920, 1080).Count;
        if (map.ActiveCount != 0 || packets.ActiveCount != 0)
            throw new Exception("Stuck pointer at stress test end");
        timer.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Console.WriteLine(
            $"PTP_PACKET_STRESS_PASS {frames} frames, {encoded} packets, " +
            $"{timer.Elapsed.TotalMilliseconds:F0}ms, {bytes / (1024.0 * 1024):F1} MiB allocated");

        var assembler = new TouchpadFrameAssembler();
        int assembled = 0;
        for (int scan = 1; scan <= 20_000; ++scan)
        {
            int fingers = scan % 11 == 0 ? 0 : (scan % 5) + 1;
            if (fingers == 0)
            {
                if (assembler.Accept(0, (uint)scan,
                      Array.Empty<TouchpadHidSample>()) is not { Length: 0 })
                    throw new Exception("Empty frame omitted");
                ++assembled;
                continue;
            }

            for (int index = 0; index < fingers; index++)
            {
                var report = assembler.Accept(index == 0 ? (uint)fingers : 0,
                    (uint)scan,
                    new[] { new TouchpadHidSample(index + 1, 300 + index * 400,
                                                   900 + index * 12, true) });
                if (index < fingers - 1 && report != null)
                    throw new Exception("Partial frame emitted as complete");
                if (index == fingers - 1)
                {
                    if (report == null || report.Length != fingers)
                        throw new Exception("Hybrid frame missing contacts");
                    ++assembled;
                }
            }
        }
        if (assembled != 20_000 || assembler.AwaitingContinuation)
            throw new Exception("Assembler stress did not finish cleanly");
        Console.WriteLine("PTP_HYBRID_STRESS_PASS 20000 frames with variable contacts");
    }
}

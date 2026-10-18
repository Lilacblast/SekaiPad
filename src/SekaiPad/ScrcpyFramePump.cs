using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using Android.Emulation.Control;

namespace SekaiPad;

/// <summary>Time-stamped input for measuring local queue/pump delay only.</summary>
public readonly record struct ScrcpyQueuedFrame(TouchEvent Frame, long QueuedAtTicks);

/// <summary>
/// Sends complete touch frames as ordered TCP byte batches. On cancellation
/// of a session, discard queued gameplay frames and release only contacts
/// that the writer actually emitted to scrcpy.
/// </summary>
public static class ScrcpyFramePump
{
    public static async Task RunAsync(
        Stream stream,
        ChannelReader<ScrcpyQueuedFrame> reader,
        ScrcpyTouchPackets codec,
        ushort width,
        ushort height,
        Func<bool> discardPending,
        Action<int> packetsWritten,
        Action<long> framePumped)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(discardPending);
        ArgumentNullException.ThrowIfNull(packetsWritten);
        ArgumentNullException.ThrowIfNull(framePumped);

        await foreach (var queued in reader.ReadAllAsync().ConfigureAwait(false))
        {
            // Releasing the currently active contacts takes precedence over
            // sending stale notes from a saturated/disconnected queue.
            if (discardPending())
                break;

            var packets = codec.Translate(queued.Frame, width, height);
            if (packets.Count > 0)
            {
                // One stream write per complete frame: same scrcpy v5 bytes,
                // same UP-before-DOWN order, fewer small socket writes.
                await stream.WriteAsync(ScrcpyTouchPackets.Batch(packets))
                    .ConfigureAwait(false);
                packetsWritten(packets.Count);
            }
            // This includes local queueing, serialization, and stream.WriteAsync
            // when packets were sent. It is not Android delivery or game latency.
            var ageUs = (long)Stopwatch.GetElapsedTime(queued.QueuedAtTicks).TotalMicroseconds;
            framePumped(Math.Max(0, ageUs));
        }

        var releases = codec.ReleaseAll(width, height);
        if (releases.Count > 0)
        {
            await stream.WriteAsync(ScrcpyTouchPackets.Batch(releases))
                .ConfigureAwait(false);
            packetsWritten(releases.Count);
        }
        await stream.FlushAsync().ConfigureAwait(false);
    }
}

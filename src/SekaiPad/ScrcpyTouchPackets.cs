using System.Buffers.Binary;
using Android.Emulation.Control;

namespace SekaiPad;

/// <summary>
/// Serializes the scrcpy v5.0 control protocol's 32-byte INJECT_TOUCH_EVENT.
/// Maintains stable finger IDs across full-frame TouchEvents produced by TouchMapping.
/// scrcpy-server translates subsequent DOWN/UP to Android POINTER_DOWN/POINTER_UP.
/// </summary>
public sealed class ScrcpyTouchPackets
{
    private readonly Dictionary<long, (int X, int Y)> _active = new();

    public int ActiveCount => _active.Count;

    public IReadOnlyList<byte[]> Translate(TouchEvent frame, ushort width, ushort height)
    {
        if (width == 0 || height == 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        var packets = new List<byte[]>();

        // Send UP before DOWN in a frame to preserve the Android pointer indices.
        foreach (Touch touch in frame.Touches)
        {
            if (touch.Pressure > 0) continue;
            long id = touch.Identifier;
            if (_active.Remove(id, out var old))
                packets.Add(Pack(1, id, old.X, old.Y, width, height));
        }
        foreach (Touch touch in frame.Touches)
        {
            if (touch.Pressure <= 0) continue;
            long id = touch.Identifier;
            if (id < 0) continue;
            int x = Math.Clamp(touch.X, 0, width - 1);
            int y = Math.Clamp(touch.Y, 0, height - 1);
            if (!_active.TryGetValue(id, out var old))
                packets.Add(Pack(0, id, x, y, width, height));
            else if (old.X != x || old.Y != y)
                packets.Add(Pack(2, id, x, y, width, height));
            _active[id] = (x, y);
        }
        return packets;
    }

    public IReadOnlyList<byte[]> ReleaseAll(ushort width, ushort height)
    {
        var packets = new List<byte[]>();
        foreach (var (id, position) in _active.Reverse())
            packets.Add(Pack(1, id, position.X, position.Y, width, height));
        _active.Clear();
        return packets;
    }

    /// <summary>
    /// Preserve scrcpy packet boundaries in the byte stream while sending a
    /// multi-contact HID frame in a single TCP write.
    /// </summary>
    public static byte[] Batch(IReadOnlyList<byte[]> packets)
    {
        ArgumentNullException.ThrowIfNull(packets);
        if (packets.Count == 0) return Array.Empty<byte>();
        const int PacketBytes = 32;
        if (packets.Count == 1)
        {
            if (packets[0].Length != PacketBytes)
                throw new ArgumentException("Invalid scrcpy touch packet length.", nameof(packets));
            return packets[0];
        }

        var batch = new byte[checked(packets.Count * PacketBytes)];
        for (int i = 0; i < packets.Count; ++i)
        {
            if (packets[i].Length != PacketBytes)
                throw new ArgumentException("Invalid scrcpy touch packet length.", nameof(packets));
            Buffer.BlockCopy(packets[i], 0, batch, i * PacketBytes, PacketBytes);
        }
        return batch;
    }

    public static byte[] Pack(byte action, long identifier, int x, int y,
                              ushort width, ushort height)
    {
        if (action is not (0 or 1 or 2))
            throw new ArgumentOutOfRangeException(nameof(action));
        var buffer = new byte[32];
        buffer[0] = 2; // TYPE_INJECT_TOUCH_EVENT
        buffer[1] = action;
        BinaryPrimitives.WriteUInt64BigEndian(buffer.AsSpan(2, 8), (ulong)identifier);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(10, 4), x);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(14, 4), y);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(18, 2), width);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(20, 2), height);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(22, 2),
                                               action == 1 ? (ushort)0 : ushort.MaxValue);
        // action_button and buttons remain 0 for fingers.
        return buffer;
    }
}

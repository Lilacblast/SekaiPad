using Android.Emulation.Control;
using RawInput.Touchpad;

namespace SekaiPad;

/// <summary>
/// Maps stable Precision Touchpad contact IDs to emulator pixel coordinates.
/// FullScreen is a 1:1 normalized absolute map: touching any place on
/// the pad touches the corresponding point of the displayed game screen.
/// Optional GameLanes mode concentrates the Y range near the judgment line.
/// </summary>
public enum TouchMappingMode { GameLanes, FullScreen }

public sealed class TouchMapping
{
    public int ScreenWidth { get; init; } = 1280;
    public int ScreenHeight { get; init; } = 720;
    public TouchMappingMode Mode { get; init; } = TouchMappingMode.FullScreen;
    public double LeftMargin { get; init; } = 0.07;
    public double RightMargin { get; init; } = 0.07;
    public double JudgementLineY { get; init; } = 0.82;
    public double VerticalSpan { get; init; } = 0.34;

    private readonly Dictionary<int, Touch> _last = new();

    public int ActiveCount => _last.Count;

    public (int X, int Y) Map(TouchpadContact contact, int minX, int maxX, int minY, int maxY)
    {
        double nx = Norm(contact.X, minX, maxX);
        double ny = Norm(contact.Y, minY, maxY);
        double usable = 1 - LeftMargin - RightMargin;
        int x = Mode == TouchMappingMode.FullScreen
            ? (int)Math.Round((ScreenWidth - 1) * nx)
            : (int)Math.Round(ScreenWidth * (LeftMargin + usable * nx));
        int y = Mode == TouchMappingMode.FullScreen
            ? (int)Math.Round((ScreenHeight - 1) * ny)
            : (int)Math.Round(ScreenHeight * (JudgementLineY + VerticalSpan * (ny - .5)));
        return (Math.Clamp(x, 0, ScreenWidth - 1), Math.Clamp(y, 0, ScreenHeight - 1));
    }

    private static double Norm(int value, int min, int max) =>
        max > min ? Math.Clamp((double)(value - min) / (max - min), 0, 1) : 0.5;

    public TouchEvent? Update(IReadOnlyList<TouchpadContact> contacts,
        int minX, int maxX, int minY, int maxY)
    {
        var frame = new TouchEvent { Display = 0 };
        var current = new Dictionary<int, Touch>();
        foreach (var contact in contacts)
        {
            if (contact.ContactId < 0 || contact.ContactId > 65535)
                continue;
            var (x, y) = Map(contact, minX, maxX, minY, maxY);
            current[contact.ContactId] = new Touch
            {
                Identifier = contact.ContactId,
                X = x,
                Y = y,
                Pressure = 80,
                TouchMajor = 7,
                TouchMinor = 5,
            };
        }

        // Explicitly send pressure=0 for released IDs. Otherwise Android
        // may keep a ghost finger held until the emulator's 120s timeout.
        foreach (var kv in _last)
            if (!current.ContainsKey(kv.Key))
                frame.Touches.Add(new Touch
                {
                    Identifier = kv.Key,
                    X = kv.Value.X,
                    Y = kv.Value.Y,
                    Pressure = 0,
                });

        foreach (var kv in current)
            frame.Touches.Add(kv.Value);

        _last.Clear();
        foreach (var kv in current)
            _last[kv.Key] = kv.Value;
        return frame.Touches.Count == 0 ? null : frame;
    }

    public TouchEvent? ReleaseAll()
    {
        if (_last.Count == 0)
            return null;
        var frame = new TouchEvent { Display = 0 };
        foreach (var kv in _last)
            frame.Touches.Add(new Touch
            {
                Identifier = kv.Key,
                X = kv.Value.X,
                Y = kv.Value.Y,
                Pressure = 0
            });
        _last.Clear();
        return frame;
    }
}

using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using RawInput.Touchpad;

namespace SekaiPad;

public readonly record struct OverlayPixel(int X, int Y);

public interface IHostTouchOverlay
{
    bool Active { get; }
    void Enable();
    void Refresh();
    void Render(IReadOnlyList<TouchpadContact> contacts, TouchMapping mapping,
        int minX, int maxX, int minY, int maxY);
    void Disable();
}

// Host-side overlay is independent from Android's developer "Show taps":
// this paints the exact mapped HID coordinates over MuMu's display child.
// It is transparent, no-activate, click-through, and never sends input.
public sealed class MuMuHostTouchOverlay : IHostTouchOverlay
{
    private const int GaRoot = 2;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int GwlExStyle = -20;

    // Screen-space positions are saved at game resolution. The renderer may
    // resize without a new HID report; viewport conversion happens in Draw().
    private readonly Dictionary<int, (int X, int Y)> _positions = new();
    private readonly Dictionary<int, (Ellipse Circle, TextBlock Label)> _markers = new();
    private bool _dirty = true;
    private bool _haveViewport;
    private int _lastLeft, _lastTop, _lastWidth, _lastHeight;
    private double _lastDpiX, _lastDpiY;
    private int _gameWidth = 1920, _gameHeight = 1080;
    private Window? _window;
    private Canvas? _canvas;
    private IntPtr _target;
    private bool _active;
    public bool Active => _active;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr param);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newValue);

    public static OverlayPixel MapGameToClient(int gameX, int gameY,
        int gameWidth, int gameHeight, int clientWidth, int clientHeight)
    {
        if (gameWidth < 1 || gameHeight < 1 || clientWidth < 1 || clientHeight < 1)
            throw new ArgumentOutOfRangeException(nameof(gameWidth));
        return new OverlayPixel(
            (int)Math.Round(Math.Clamp(gameX, 0, gameWidth - 1) *
                            (double)(clientWidth - 1) / Math.Max(1, gameWidth - 1)),
            (int)Math.Round(Math.Clamp(gameY, 0, gameHeight - 1) *
                            (double)(clientHeight - 1) / Math.Max(1, gameHeight - 1)));
    }

    private static IntPtr FindMuMuDisplay()
    {
        // Never apply an overlay to a similarly named arbitrary game window.
        // We only match the MuMu renderer child class and known process name.
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("MuMuNxDevice"))
        {
            using (process)
            {
                var root = process.MainWindowHandle;
                if (root == IntPtr.Zero || !IsWindow(root)) continue;
                IntPtr match = IntPtr.Zero;
                EnumChildWindows(root, (hwnd, _) =>
                {
                    var className = new StringBuilder(128);
                    GetClassName(hwnd, className, className.Capacity);
                    var title = new StringBuilder(128);
                    GetWindowText(hwnd, title, title.Capacity);
                    if (className.ToString() == "nemuwin" && title.ToString() == "nemudisplay")
                    {
                        if (GetWindowThreadProcessId(hwnd, out uint childPid) != 0 &&
                            childPid == process.Id)
                        {
                            match = hwnd;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
                if (match != IntPtr.Zero) return match;
            }
        }
        return IntPtr.Zero;
    }

    public void Enable()
    {
        if (_active) return;
        _target = FindMuMuDisplay();
        if (_target == IntPtr.Zero)
            throw new InvalidOperationException(
                "MuMu game viewport not found. Cannot align touch indicators.");
        _positions.Clear();
        _markers.Clear();
        _haveViewport = false;
        _dirty = true;
        _canvas = new Canvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };
        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Content = _canvas,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            Topmost = true,
            IsHitTestVisible = false,
            Width = 2,
            Height = 2,
            Left = 0,
            Top = 0
        };
        try
        {
            _window.Show();
            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            int originalStyle = unchecked((int)GetWindowLongPtr(hwnd, GwlExStyle).ToInt64());
            int newStyle = originalStyle | WsExTransparent | WsExNoActivate | WsExToolWindow;
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(newStyle));
            _active = true;
            Refresh();
        }
        catch
        {
            Disable();
            throw;
        }
    }

    private bool TryGetViewport(out Rect rect)
    {
        rect = default;
        if (!_active || !IsWindow(_target) || !GetWindowRect(_target, out rect))
            return false;
        if (rect.Width <= 2 || rect.Height <= 2) return false;
        IntPtr gameRoot = GetAncestor(_target, GaRoot);
        return GetAncestor(GetForegroundWindow(), GaRoot) == gameRoot;
    }

    public void Refresh()
    {
        if (!_active || _window == null || _canvas == null) return;
        if (!TryGetViewport(out Rect rect))
        {
            if (_window.IsVisible) _window.Hide();
            // Never leave ghost finger markers when switching away from MuMu.
            if (_positions.Count > 0)
            {
                _positions.Clear();
                _dirty = true;
            }
            _haveViewport = false;
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        double dpiX = dpi.DpiScaleX, dpiY = dpi.DpiScaleY;
        bool resized = !_haveViewport ||
            _lastLeft != rect.Left || _lastTop != rect.Top ||
            _lastWidth != rect.Width || _lastHeight != rect.Height ||
            _lastDpiX != dpiX || _lastDpiY != dpiY;
        if (resized)
        {
            _window.Left = rect.Left / dpiX;
            _window.Top = rect.Top / dpiY;
            _window.Width = rect.Width / dpiX;
            _window.Height = rect.Height / dpiY;
            _lastLeft = rect.Left;
            _lastTop = rect.Top;
            _lastWidth = rect.Width;
            _lastHeight = rect.Height;
            _lastDpiX = dpiX;
            _lastDpiY = dpiY;
            _haveViewport = true;
        }
        if (!_window.IsVisible)
        {
            _window.Show();
            _dirty = true;
        }
        if (!_dirty && !resized) return;
        Draw(rect.Width, rect.Height, dpiX, dpiY);
        _dirty = false;
    }

    // Update only changed markers. Recreating all WPF children per raw HID
    // packet caused excessive UI layout/composition work during gameplay.
    private void Draw(int width, int height, double dpiX, double dpiY)
    {
        if (_canvas == null) return;
        foreach (var id in _markers.Keys.ToArray())
        {
            if (_positions.ContainsKey(id)) continue;
            var previous = _markers[id];
            _canvas.Children.Remove(previous.Circle);
            _canvas.Children.Remove(previous.Label);
            _markers.Remove(id);
        }
        foreach (var (id, p) in _positions)
        {
            if (!_markers.TryGetValue(id, out var marker))
            {
                var circle = new Ellipse
                {
                    Fill = new SolidColorBrush(Color.FromArgb(125, 180, 132, 245)),
                    Stroke = Brushes.White,
                    StrokeThickness = 2.2,
                    IsHitTestVisible = false
                };
                var label = new TextBlock
                {
                    Text = id.ToString(),
                    FontSize = 11,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    IsHitTestVisible = false
                };
                marker = (circle, label);
                _markers[id] = marker;
                _canvas.Children.Add(circle);
                _canvas.Children.Add(label);
            }
            var px = MapGameToClient(p.X, p.Y, _gameWidth,
                _gameHeight, width, height);
            double x = px.X / dpiX;
            double y = px.Y / dpiY;
            double radius = Math.Max(12, width / 1920.0 * 24) / dpiX;
            if (marker.Circle.Width != radius * 2)
            {
                marker.Circle.Width = radius * 2;
                marker.Circle.Height = radius * 2;
            }
            Canvas.SetLeft(marker.Circle, x - radius);
            Canvas.SetTop(marker.Circle, y - radius);
            Canvas.SetLeft(marker.Label, x + radius * .35);
            Canvas.SetTop(marker.Label, y - radius * .8);
        }
    }

    // Called on the low-priority visual timer, never inside the input sender.
    // Store only the newest touch positions; drop intermediate visual frames.
    public void Render(IReadOnlyList<TouchpadContact> contacts, TouchMapping mapping,
        int minX, int maxX, int minY, int maxY)
    {
        if (!_active) return;
        var next = new Dictionary<int, (int X, int Y)>(contacts.Count);
        foreach (var contact in contacts)
            next[contact.ContactId] = mapping.Map(contact, minX, maxX, minY, maxY);
        bool changed = _gameWidth != mapping.ScreenWidth ||
                       _gameHeight != mapping.ScreenHeight ||
                       _positions.Count != next.Count;
        if (!changed)
        {
            foreach (var (id, coords) in next)
            {
                if (!_positions.TryGetValue(id, out var old) || old != coords)
                {
                    changed = true;
                    break;
                }
            }
        }
        if (!changed) return;
        _positions.Clear();
        foreach (var (id, coords) in next)
            _positions[id] = coords;
        _gameWidth = mapping.ScreenWidth;
        _gameHeight = mapping.ScreenHeight;
        _dirty = true;
    }

    public void Disable()
    {
        _active = false;
        _positions.Clear();
        _markers.Clear();
        _haveViewport = false;
        _dirty = true;
        _canvas?.Children.Clear();
        _canvas = null;
        if (_window is { } window)
        {
            _window = null;
            window.Close();
        }
        _target = IntPtr.Zero;
    }
}

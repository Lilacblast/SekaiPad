using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace SekaiPad;

public interface IMouseInputGuard
{
    bool IsActive { get; }
    long BlockedEvents { get; }
    void Enable();
    void Disable();
}

/// <summary>
/// Captures Windows legacy mouse input during rhythm mode, without touching
/// Precision Touchpad digitizer WM_INPUT (HID 0x0D/0x05) or the keyboard.
/// This is session-wide while enabled: physical USB mice are blocked too.
/// Windows automatically removes the hook if this process exits.
/// </summary>
public sealed class LowLevelMouseGuard : IMouseInputGuard, IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmRButtonDblClk = 0x0206;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMButtonDblClk = 0x0209;
    private const int WmMouseWheel = 0x020A;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const int WmXButtonDblClk = 0x020D;
    private const int WmMouseHWheel = 0x020E;

    // The callback must stay alive for the entire native hook lifetime.
    private readonly HookProc _proc;
    private IntPtr _hook;
    private volatile bool _active;
    private long _blocked;

    public LowLevelMouseGuard()
    {
        _proc = HookCallback;
    }

    public bool IsActive => _active && _hook != IntPtr.Zero;
    public long BlockedEvents => Interlocked.Read(ref _blocked);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook, HookProc procedure, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    internal static bool IsBlockedMouseMessage(int message) =>
        message >= WmMouseMove && message <= WmMouseHWheel;

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        // Never perform UI, allocations, IPC, or logging here: Windows can
        // silently remove a slow low-level hook. The dedicated HID decoder
        // keeps receiving raw digitizer reports independently.
        if (code >= 0 && _active && IsBlockedMouseMessage(wParam.ToInt32()))
        {
            Interlocked.Increment(ref _blocked);
            return new IntPtr(1);
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Enable()
    {
        if (IsActive) return;
        // Must be installed on the WPF dispatcher / window message-loop thread.
        var module = GetModuleHandle(null);
        _hook = SetWindowsHookEx(WhMouseLl, _proc, module, 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Unable to install the rhythm-mode mouse suppression hook.");
        // Prevent the touchpad from double-generating Android touch + mouse click.
        _active = true;
    }

    public void Disable()
    {
        _active = false; // Fail open immediately, before unhooking.
        IntPtr hook = _hook;
        _hook = IntPtr.Zero;
        if (hook != IntPtr.Zero && !UnhookWindowsHookEx(hook))
        {
            // A timeout can already have removed the hook; Windows restores
            // normal input when the hook owner exits.
            int error = Marshal.GetLastWin32Error();
            if (error != 1404 /* ERROR_INVALID_HOOK_HANDLE */)
                throw new Win32Exception(error, "Unable to remove mouse hook.");
        }
    }

    public void Dispose() => Disable();
}

using Android.Emulation.Control;

namespace SekaiPad;

/// <summary>Local, independently managed touch backends. No input goes via Miku/VPS.</summary>
public interface ITouchBridge : IAsyncDisposable
{
    event Action<Exception>? Faulted;
    bool Connected { get; }
    Task ConnectAsync(string endpoint);
    bool Send(TouchEvent frame);
}

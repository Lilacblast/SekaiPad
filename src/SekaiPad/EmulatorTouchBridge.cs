using System.Threading.Channels;
using Android.Emulation.Control;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;

namespace SekaiPad;

/// <summary>
/// One persistent local-only gRPC stream; never passes time-critical
/// touch input through the Miku Hub, ADB shell or network services.
/// </summary>
public sealed class EmulatorTouchBridge : ITouchBridge
{
    private readonly Channel<InputEvent> _queue =
        Channel.CreateBounded<InputEvent>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

    private GrpcChannel? _grpc;
    private AsyncClientStreamingCall<InputEvent, Empty>? _call;
    private Task? _worker;
    private bool _connected;

    public event Action<Exception>? Faulted;
    public bool Connected => _connected;

    public async Task ConnectAsync(string endpoint)
    {
        if (_connected) throw new InvalidOperationException("Already connected.");
        var uri = new Uri(endpoint);
        // Only a locally started emulator may receive our inputs.
        if (uri.Scheme != "http" ||
            !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              uri.Host == "127.0.0.1"))
            throw new InvalidOperationException("Only loopback HTTP gRPC is allowed.");

        _grpc = GrpcChannel.ForAddress(uri);
        var client = new EmulatorController.EmulatorControllerClient(_grpc);
        // Fail clearly if the emulator is offline or the gRPC port is wrong.
        await client.getStatusAsync(new Empty(), deadline: DateTime.UtcNow.AddSeconds(8));
        _call = client.streamInputEvent();
        _connected = true;
        _worker = Task.Run(PumpAsync);
    }

    public bool Send(TouchEvent touches)
    {
        if (!_connected) return false;
        return _queue.Writer.TryWrite(new InputEvent { TouchEvent = touches });
    }

    private async Task PumpAsync()
    {
        try
        {
            if (_call == null) return;
            await foreach (var next in _queue.Reader.ReadAllAsync())
                await _call.RequestStream.WriteAsync(next);

            await _call.RequestStream.CompleteAsync();
            await _call.ResponseAsync;
        }
        catch (Exception ex)
        {
            _connected = false;
            _queue.Writer.TryComplete(ex);
            Faulted?.Invoke(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _connected = false;
        _queue.Writer.TryComplete();
        if (_worker != null)
            await Task.WhenAny(_worker, Task.Delay(1500));
        _call?.Dispose();
        _grpc?.Dispose();
    }
}

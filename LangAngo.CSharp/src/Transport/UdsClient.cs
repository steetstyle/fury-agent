using System.Net.Sockets;
using System.Text;
using LangAngo.CSharp;
using LangAngo.CSharp.Core;

namespace LangAngo.CSharp.Transport;

public sealed class UdsClient : IAsyncDisposable
{
    private readonly string _socketPath;
    private Socket? _socket;
    private NetworkStream? _stream;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _connected;
    private readonly object _writeLock = new();

    public static UdsClient? Instance { get; private set; }
    public bool IsConnected => _connected && _socket != null && _stream != null;

    public UdsClient(string socketPath = "/tmp/langango.sock")
    {
        _socketPath = socketPath;
        Instance = this;
    }

    public async Task ConnectAsync()
    {
        if (_socket != null) return;

        await Task.Delay(100);

        _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
        
        try 
        {
            await _socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath));
            _stream = new NetworkStream(_socket, ownsSocket: true);
            
            Logger.Info("Connected to Unix Socket: {0}", _socketPath);
            
            _connected = true;
            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessChannelAsync(_stream!), _cts.Token);
        }
        catch (Exception ex)
        {
            Logger.Error("Connection failed: {0}", ex.Message);
        }
    }

    private async Task ProcessChannelAsync(NetworkStream stream)
    {
        var token = _cts?.Token ?? CancellationToken.None;
        
        try
        {
            Logger.Verbose("ProcessChannelAsync started, waiting for spans...");
            await foreach (var span in SpanChannel.Reader.ReadAllAsync(token))
            {
                try
                {
                    if (!IsConnected)
                    {
                        Logger.Warning("Cannot write span - not connected");
                        continue;
                    }

                    var bytes = SerializeSpan(span);
                    
                    lock (_writeLock)
                    {
                        stream.Write(bytes);
                        stream.Flush();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Write error: {0}", ex.Message);
                    _connected = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("Channel processing error: {0}", ex.Message);
            _connected = false;
        }
    }

    private byte[] SerializeSpan(Span span)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((byte)Protocol.Magic1);
        writer.Write((byte)Protocol.Magic2);
        writer.Write(Protocol.Version);
        writer.Write((byte)span.Type);

        var nameBytes = Encoding.UTF8.GetBytes(span.Name);
        var metadataDict = span.Metadata?.ToDictionary(k => k.Key, v => v.Value) ?? new Dictionary<string, string>();
        var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadataDict);
        var metadataBytes = Encoding.UTF8.GetBytes(metadataJson);

        var payloadLength = 4 + nameBytes.Length + 4 + metadataBytes.Length;
        
        var payloadLenBytes = BitConverter.GetBytes((uint)payloadLength);
        if (BitConverter.IsLittleEndian) Array.Reverse(payloadLenBytes);
        writer.Write(payloadLenBytes);

        var traceIdBytes = span.TraceId.ToByteArray();
        writer.Write(traceIdBytes);

        var spanIdBytes = BitConverter.GetBytes(span.SpanId);
        if (BitConverter.IsLittleEndian) Array.Reverse(spanIdBytes);
        writer.Write(spanIdBytes);
        
        var parentId = span.ParentId ?? 0;
        var parentIdBytes = BitConverter.GetBytes(parentId);
        if (BitConverter.IsLittleEndian) Array.Reverse(parentIdBytes);
        writer.Write(parentIdBytes);

        writer.Write(nameBytes.Length);
        writer.Write(nameBytes);
        writer.Write(metadataBytes.Length);
        writer.Write(metadataBytes);

        writer.Flush();
        return ms.ToArray();
    }

    public void Send(byte[] data)
    {
        if (!IsConnected) return;
        
        lock (_writeLock)
        {
            try
            {
                _stream!.Write(data);
                _stream!.Flush();
            }
            catch (Exception ex)
            {
                Logger.Error("Send error: {0}", ex.Message);
                _connected = false;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        
        if (_processingTask != null)
        {
            try
            {
                await _processingTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch { }
        }

        _stream?.Dispose();
        _socket?.Dispose();
        _cts?.Dispose();
    }
}

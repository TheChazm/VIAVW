using System.Net.WebSockets;
using System.Text.Json;

namespace LayerHost;

internal sealed class LocalCdp : IDisposable
{
    readonly ClientWebSocket socket = new(); int sequence;
    internal LocalCdp(string url)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "ws" || uri.Host != "127.0.0.1") throw new InvalidOperationException("Require a loopback CDP endpoint.");
        using var deadline = new CancellationTokenSource(5000); socket.ConnectAsync(uri, deadline.Token).GetAwaiter().GetResult();
    }
    internal JsonElement Call(string method, object parameters, string? session = null)
    {
        using var deadline = new CancellationTokenSource(5000); int id = ++sequence;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters, sessionId = session }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, deadline.Token).GetAwaiter().GetResult();
        var buffer = new byte[65536];
        while (true)
        {
            using var message = new MemoryStream(); WebSocketReceiveResult part;
            do { part = socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token).GetAwaiter().GetResult(); if (part.MessageType == WebSocketMessageType.Close) throw new IOException("CDP closed"); message.Write(buffer, 0, part.Count); if (message.Length > 32_000_000) throw new IOException("CDP response too large"); } while (!part.EndOfMessage);
            using var doc = JsonDocument.Parse(message.ToArray()); var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var response) || response.GetInt32() != id) continue;
            if (root.TryGetProperty("error", out var error)) throw new LayerOperationException(error.ToString());
            return root.GetProperty("result").Clone();
        }
    }
    public void Dispose() => socket.Dispose();
}

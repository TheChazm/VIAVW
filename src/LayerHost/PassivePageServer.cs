using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LayerHost;

// Disposable loopback page and passive observation endpoint. It cannot issue input.
internal sealed class PassivePageServer : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stop = new();
    readonly Task worker;
    readonly byte[] html;
    readonly object gate = new();
    JsonElement? latest;
    internal string Url { get; }
    internal JsonElement? Observation { get { lock (gate) return latest?.Clone(); } }
    internal PassivePageServer(string page)
    {
        html = Encoding.UTF8.GetBytes(page); listener.Start(8); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
        worker = Task.Run(Run);
    }
    async Task Run()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); deadline.CancelAfter(2000);
                try { await Serve(client.GetStream(), deadline.Token); }
                catch (Exception e) when (e is IOException or OperationCanceledException or JsonException or FormatException) { }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    async Task Serve(NetworkStream stream, CancellationToken token)
    {
        using var bytes = new MemoryStream(); var buffer = new byte[4096]; int headerEnd = -1;
        while (headerEnd < 0 && bytes.Length < 16384)
        {
            int count = await stream.ReadAsync(buffer, token); if (count == 0) return; bytes.Write(buffer, 0, count);
            var data = bytes.GetBuffer(); for (int i = 0; i + 3 < bytes.Length; i++) if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10) { headerEnd = i + 4; break; }
        }
        if (headerEnd < 0) return;
        string[] headers = Encoding.ASCII.GetString(bytes.GetBuffer(), 0, headerEnd).Split("\r\n"); bool post = headers[0].StartsWith("POST /observed ", StringComparison.Ordinal);
        if (post)
        {
            string? lengthHeader = headers.FirstOrDefault(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            if (lengthHeader == null || !int.TryParse(lengthHeader[15..].Trim(), out int length) || length is < 1 or > 65536) return;
            while (bytes.Length - headerEnd < length) { int count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, length - (int)(bytes.Length - headerEnd))), token); if (count == 0) return; bytes.Write(buffer, 0, count); }
            using var document = JsonDocument.Parse(bytes.GetBuffer().AsMemory(headerEnd, length)); lock (gate) latest = document.RootElement.Clone();
        }
        bool page = headers[0].StartsWith("GET / ", StringComparison.Ordinal); byte[] body = page ? html : [];
        byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nCache-Control: no-store\r\nContent-Type: " + (page ? "text/html; charset=utf-8" : "text/plain") + "\r\nContent-Length: " + body.Length + "\r\n\r\n");
        await stream.WriteAsync(response, token); await stream.WriteAsync(body, token);
    }
    public void Dispose()
    {
        stop.Cancel(); listener.Stop(); worker.GetAwaiter().GetResult(); stop.Dispose();
    }
}

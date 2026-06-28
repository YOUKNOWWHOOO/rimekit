using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RimeKit.Windows.Tests;

internal sealed class SimpleHttpServer : IDisposable
{
    public delegate (int StatusCode, byte[] Payload, string ContentType, Dictionary<string, string>? Headers)? RouteHandler(
        string method, string path, byte[]? body, IReadOnlyDictionary<string, string>? requestHeaders);

    private readonly TcpListener _listener;
    private readonly Func<string, string, (byte[] Payload, string ContentType)?>? _simpleHandler;
    private readonly RouteHandler? _routeHandler;
    private volatile bool _stopped;
    private bool _includeContentLength = true;

    public string BaseUrl { get; }

    private SimpleHttpServer(TcpListener listener, Func<string, string, (byte[] Payload, string ContentType)?> handler)
    {
        _listener = listener;
        _simpleHandler = handler;
        var ep = (IPEndPoint)listener.LocalEndpoint;
        BaseUrl = $"http://127.0.0.1:{ep.Port}/";
    }

    private SimpleHttpServer(TcpListener listener, RouteHandler handler)
    {
        _listener = listener;
        _routeHandler = handler;
        var ep = (IPEndPoint)listener.LocalEndpoint;
        BaseUrl = $"http://127.0.0.1:{ep.Port}/";
    }

    public static SimpleHttpServer Create(byte[] payload, string contentType = "application/octet-stream")
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new SimpleHttpServer(listener, (_, _) => (payload, contentType));
    }

    public static SimpleHttpServer CreateWithoutContentLength(byte[] payload, string contentType = "application/octet-stream")
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = new SimpleHttpServer(listener, (_, _) => (payload, contentType));
        server._includeContentLength = false;
        return server;
    }

    public static SimpleHttpServer Create(RouteHandler handler)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new SimpleHttpServer(listener, handler);
    }

    public static SimpleHttpServer CreateWithoutContentLength(RouteHandler handler)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = new SimpleHttpServer(listener, handler);
        server._includeContentLength = false;
        return server;
    }

    public void Serve(int maxRequests)
    {
        if (_routeHandler is not null)
        {
            ServeRoutes(maxRequests);
        }
        else
        {
            ServeSimple(maxRequests);
        }
    }

    private void ServeSimple(int maxRequests)
    {
        int served = 0;
        byte[] buf = new byte[8192];
        while (served < maxRequests && !_stopped)
        {
            try
            {
                using var client = _listener.AcceptTcpClient();
                served++;
                using var stream = client.GetStream();
                stream.ReadTimeout = 5000;
                int bytesRead = stream.Read(buf, 0, buf.Length);
                if (bytesRead == 0) continue;

                string text = Encoding.ASCII.GetString(buf, 0, bytesRead);
                int bodyStart = text.IndexOf("\r\n\r\n");
                if (bodyStart < 0) continue;

                string[] lines = text[..bodyStart].Split("\r\n");
                if (lines.Length == 0) continue;

                string[] parts = lines[0].Split(' ');
                if (parts.Length < 2) continue;

                string method = parts[0];
                string rawPath = parts[1];
                int qi = rawPath.IndexOf('?');
                string path = qi >= 0 ? rawPath[..qi] : rawPath;

                var result = _simpleHandler!(method, path);
                if (result is null) continue;

                var (payload, contentType) = result.Value;
                string response;
                if (_includeContentLength)
                    response = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                else
                    response = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nConnection: close\r\n\r\n";
                byte[] headerBytes = Encoding.ASCII.GetBytes(response);
                stream.Write(headerBytes);
                if (!string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
                    stream.Write(payload);
                stream.Flush();
            }
            catch (SocketException) { break; }
            catch (IOException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private void ServeRoutes(int maxRequests)
    {
        int served = 0;
        byte[] buf = new byte[65536];
        while (served < maxRequests && !_stopped)
        {
            try
            {
                using var client = _listener.AcceptTcpClient();
                served++;
                using var stream = client.GetStream();
                stream.ReadTimeout = 10000;
                int totalRead = 0;
                int chunkEnd = -1;
                while (totalRead < buf.Length)
                {
                    int n = stream.Read(buf, totalRead, buf.Length - totalRead);
                    if (n == 0) break;
                    totalRead += n;
                    string head = Encoding.ASCII.GetString(buf, 0, totalRead);
                    chunkEnd = head.IndexOf("\r\n\r\n");
                    if (chunkEnd >= 0) break;
                }
                if (totalRead == 0 || chunkEnd < 0) continue;

                string headerText = Encoding.ASCII.GetString(buf, 0, chunkEnd);
                string[] lines = headerText.Split("\r\n");
                if (lines.Length == 0) continue;

                string[] requestLine = lines[0].Split(' ');
                if (requestLine.Length < 2) continue;
                string method = requestLine[0];
                string rawPath = requestLine[1];
                int qi = rawPath.IndexOf('?');
                string path = qi >= 0 ? rawPath[..qi] : rawPath;

                var reqHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    int colon = lines[i].IndexOf(':');
                    if (colon > 0 && colon < lines[i].Length - 1)
                    {
                        reqHeaders[lines[i][..colon]] = lines[i][(colon + 1)..].Trim();
                    }
                }

                int contentLength = 0;
                if (reqHeaders.TryGetValue("Content-Length", out string? cl) && int.TryParse(cl, out int parsed))
                    contentLength = parsed;

                byte[]? body = null;
                if (contentLength > 0)
                {
                    int bodyOffset = chunkEnd + 4;
                    int available = totalRead - bodyOffset;
                    while (available < contentLength)
                    {
                        int n = stream.Read(buf, totalRead, buf.Length - totalRead);
                        if (n == 0) break;
                        totalRead += n;
                        available = totalRead - bodyOffset;
                    }
                    if (available >= contentLength)
                    {
                        body = new byte[contentLength];
                        Array.Copy(buf, bodyOffset, body, 0, contentLength);
                    }
                }

                var result = _routeHandler!(method, path, body, reqHeaders);
                if (result is null) continue;

                var (statusCode, payload, contentType, extraHeaders) = result.Value;
                string statusText = statusCode switch
                {
                    200 => "200 OK",
                    404 => "404 Not Found",
                    500 => "500 Internal Server Error",
                    _ => $"{statusCode} Unknown",
                };

                var responseBuilder = new StringBuilder();
                responseBuilder.Append($"HTTP/1.1 {statusText}\r\n");
                responseBuilder.Append($"Content-Type: {contentType}\r\n");
                responseBuilder.Append("Connection: close\r\n");
                if (_includeContentLength)
                    responseBuilder.Append($"Content-Length: {payload.Length}\r\n");
                if (extraHeaders is not null)
                {
                    foreach (var kv in extraHeaders)
                        responseBuilder.Append($"{kv.Key}: {kv.Value}\r\n");
                }
                responseBuilder.Append("\r\n");

                byte[] headerBytes = Encoding.ASCII.GetBytes(responseBuilder.ToString());
                stream.Write(headerBytes);
                if (statusCode >= 200 && statusCode != 204 && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
                    stream.Write(payload);
                stream.Flush();
            }
            catch (SocketException) { break; }
            catch (IOException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    public void Dispose()
    {
        _stopped = true;
        try { _listener.Stop(); } catch (ObjectDisposedException) { }
    }
}

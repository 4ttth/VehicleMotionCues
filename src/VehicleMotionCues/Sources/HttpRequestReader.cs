using System.Text;

namespace VehicleMotionCues.Sources;

public sealed class HttpRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required Dictionary<string, string> Query { get; init; }
    public required Dictionary<string, string> Headers { get; init; }
    public byte[] Body { get; init; } = [];

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;

    public bool IsWebSocketUpgrade =>
        string.Equals(Header("upgrade"), "websocket", StringComparison.OrdinalIgnoreCase) &&
        Header("sec-websocket-key") is not null;

    public bool WantsClose => string.Equals(Header("connection"), "close", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Just enough HTTP/1.1 parsing for the phone page: GET, small POSTs, keep-alive, WebSocket upgrade.</summary>
public sealed class HttpRequestReader(Stream stream)
{
    const int MaxHeaderBytes = 16 * 1024;
    const int MaxBodyBytes = 1024 * 1024;

    readonly byte[] _buffer = new byte[MaxHeaderBytes];
    int _count;

    /// <returns>The next request, or null if the client closed the connection cleanly.</returns>
    public async Task<HttpRequest?> ReadAsync(CancellationToken ct)
    {
        int headerEnd;
        while ((headerEnd = IndexOfHeaderEnd()) < 0)
        {
            if (_count == _buffer.Length)
                throw new InvalidDataException("Request headers too large.");
            int read = await stream.ReadAsync(_buffer.AsMemory(_count), ct);
            if (read == 0)
            {
                if (_count == 0)
                    return null;
                throw new EndOfStreamException();
            }
            _count += read;
        }

        var lines = Encoding.ASCII.GetString(_buffer, 0, headerEnd).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
            throw new InvalidDataException("Bad request line.");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        string target = requestLine[1];
        int q = target.IndexOf('?');
        string path = q < 0 ? target : target[..q];
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (q >= 0)
        {
            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
                query[key] = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }

        Consume(headerEnd + 4);

        byte[] body = [];
        if (headers.TryGetValue("content-length", out var lengthText) && int.TryParse(lengthText, out int length) && length > 0)
        {
            if (length > MaxBodyBytes)
                throw new InvalidDataException("Body too large.");
            body = new byte[length];
            int fromBuffer = Math.Min(length, _count);
            Array.Copy(_buffer, body, fromBuffer);
            Consume(fromBuffer);
            await stream.ReadExactlyAsync(body.AsMemory(fromBuffer), ct);
        }

        return new HttpRequest { Method = requestLine[0], Path = path, Query = query, Headers = headers, Body = body };
    }

    int IndexOfHeaderEnd() => _buffer.AsSpan(0, _count).IndexOf("\r\n\r\n"u8);

    void Consume(int n)
    {
        Array.Copy(_buffer, n, _buffer, 0, _count - n);
        _count -= n;
    }
}

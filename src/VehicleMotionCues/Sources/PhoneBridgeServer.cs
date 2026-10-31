using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Numerics;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using VehicleMotionCues.Motion;

namespace VehicleMotionCues.Sources;

/// <summary>
/// Serves the phone page over HTTPS and receives its motion samples.
///
/// iOS Safari only exposes DeviceMotion to secure pages, so the laptop runs a tiny TLS server with
/// a self-signed certificate (the user taps through Safari's warning once). Samples arrive over a
/// WebSocket, or as batched POSTs if Safari refuses the WebSocket for the self-signed certificate.
///
/// Each sample is [timeMs, ax, ay, az, upX, upY, upZ]: gravity-free acceleration in m/s² and an
/// "up" vector, both in the phone's own frame, already normalised to the W3C sign convention.
/// </summary>
public sealed class PhoneBridgeServer : IDisposable
{
    const int PortAttempts = 10;

    readonly MotionHub _hub;
    readonly Settings _settings;
    readonly CancellationTokenSource _cts = new();
    readonly object _solverGate = new();
    // Phone lying flat with its top toward the windscreen: +Y. Standing up, screen facing you: -Z.
    readonly VehicleFrameSolver _solver = new(new Vector3(0, 1, 0), new Vector3(0, 0, -1));
    readonly byte[] _page;

    TcpListener? _listener;
    X509Certificate2? _certificate;
    long _lastSampleTimestamp;

    public PhoneBridgeServer(MotionHub hub, Settings settings)
    {
        _hub = hub;
        _settings = settings;
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("phone.html")
            ?? throw new InvalidOperationException("phone.html resource missing.");
        using var ms = new MemoryStream();
        resource.CopyTo(ms);
        _page = ms.ToArray();
    }

    public int Port { get; private set; }

    public bool IsRunning => _listener is not null;

    public bool IsReceiving =>
        _lastSampleTimestamp != 0 && Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastSampleTimestamp)) < TimeSpan.FromSeconds(1.5);

    public string UrlFor(IPAddress address) => $"https://{address}:{Port}/?k={_settings.PairingKey}";

    public void Start()
    {
        _certificate = LoadOrCreateCertificate();

        for (int port = _settings.Port; port < _settings.Port + PortAttempts; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                _listener = listener;
                Port = port;
                break;
            }
            catch (SocketException)
            {
                // Port taken; try the next one.
            }
        }
        if (_listener is null)
            throw new InvalidOperationException($"No free port between {_settings.Port} and {_settings.Port + PortAttempts - 1}.");

        _ = Task.Run(AcceptLoopAsync);
    }

    async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    async Task HandleClientAsync(TcpClient client)
    {
        using var _ = client;
        client.NoDelay = true;
        await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        try
        {
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = false,
                }, handshake.Token);
            }

            var reader = new HttpRequestReader(ssl);
            while (true)
            {
                HttpRequest? request;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    idle.CancelAfter(TimeSpan.FromSeconds(60));
                    request = await reader.ReadAsync(idle.Token);
                }
                if (request is null)
                    return;

                bool authorised = request.Query.TryGetValue("k", out var key) && key == _settings.PairingKey;

                if (request.Path == "/ws" && request.IsWebSocketUpgrade)
                {
                    if (!authorised)
                    {
                        await WriteResponseAsync(ssl, 403, "text/plain", "Wrong pairing key. Rescan the QR code."u8.ToArray(), close: true);
                        return;
                    }
                    await RunWebSocketAsync(ssl, request);
                    return;
                }

                if (request.Method == "POST" && request.Path == "/m")
                {
                    if (authorised)
                    {
                        HandlePayload(request.Body);
                        await WriteResponseAsync(ssl, 204, null, [], request.WantsClose);
                    }
                    else
                    {
                        await WriteResponseAsync(ssl, 403, "text/plain", "Wrong pairing key."u8.ToArray(), request.WantsClose);
                    }
                }
                else if (request.Method == "GET" && request.Path is "/" or "/index.html")
                {
                    await WriteResponseAsync(ssl, 200, "text/html; charset=utf-8", _page, request.WantsClose);
                }
                else
                {
                    await WriteResponseAsync(ssl, 404, "text/plain", "Not found"u8.ToArray(), request.WantsClose);
                }

                if (request.WantsClose)
                    return;
            }
        }
        catch (Exception)
        {
            // Rejected certificates, dropped Wi-Fi and locked phones all end up here; just drop the connection.
        }
    }

    async Task RunWebSocketAsync(Stream stream, HttpRequest request)
    {
        string accept = Convert.ToBase64String(SHA1.HashData(
            Encoding.ASCII.GetBytes(request.Header("sec-websocket-key") + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var handshake = Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n");
        await stream.WriteAsync(handshake, _cts.Token);
        await stream.FlushAsync(_cts.Token);

        using var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
        {
            IsServer = true,
            KeepAliveInterval = TimeSpan.FromSeconds(10),
        });

        var buffer = new byte[8192];
        using var message = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, _cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                break;
            }
            message.Write(buffer, 0, result.Count);
            if (message.Length > 1024 * 1024)
                break;
            if (result.EndOfMessage)
            {
                HandlePayload(message.ToArray());
                message.SetLength(0);
            }
        }
    }

    void HandlePayload(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return;

            if (root[0].ValueKind == JsonValueKind.Array)
            {
                foreach (var sample in root.EnumerateArray())
                    HandleSample(sample);
            }
            else
            {
                HandleSample(root);
            }
        }
        catch (JsonException)
        {
        }
    }

    void HandleSample(JsonElement s)
    {
        if (s.ValueKind != JsonValueKind.Array || s.GetArrayLength() < 7)
            return;

        double timeSeconds = s[0].GetDouble() / 1000.0;
        var accel = new Vector3(s[1].GetSingle(), s[2].GetSingle(), s[3].GetSingle());
        var up = new Vector3(s[4].GetSingle(), s[5].GetSingle(), s[6].GetSingle());

        (double lateral, double longitudinal) result;
        lock (_solverGate)
            result = _solver.Solve(accel, up, timeSeconds);

        _hub.Report(SourceKind.Phone, result.lateral, result.longitudinal);
        Interlocked.Exchange(ref _lastSampleTimestamp, Stopwatch.GetTimestamp());
    }

    static async Task WriteResponseAsync(Stream stream, int status, string? contentType, byte[] body, bool close)
    {
        string reason = status switch { 200 => "OK", 204 => "No Content", 403 => "Forbidden", _ => "Not Found" };
        var head = new StringBuilder()
            .Append($"HTTP/1.1 {status} {reason}\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append($"Content-Length: {body.Length}\r\n")
            .Append(close ? "Connection: close\r\n" : "Connection: keep-alive\r\n");
        if (contentType is not null)
            head.Append($"Content-Type: {contentType}\r\n");
        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
        if (body.Length > 0)
            await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    /// <summary>
    /// Reuses one certificate across runs so the phone only has to accept Safari's warning once.
    /// Kept under Apple's 825-day limit and with serverAuth EKU, which iOS requires even when untrusted.
    /// </summary>
    static X509Certificate2 LoadOrCreateCertificate()
    {
        string path = Path.Combine(Settings.Directory, "bridge-certificate.pfx");
        try
        {
            if (File.Exists(path))
            {
                var existing = new X509Certificate2(File.ReadAllBytes(path), (string?)null, X509KeyStorageFlags.UserKeySet);
                if (existing.HasPrivateKey && existing.NotAfter > DateTime.Now.AddDays(14))
                    return existing;
            }
        }
        catch (CryptographicException)
        {
            // Corrupt or unreadable; regenerate below.
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Vehicle Motion Cues (this laptop)", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        foreach (var lan in NetworkInfo.GetLanAddresses())
            san.AddIpAddress(lan.Address);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));

        using var created = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(800));
        var pfx = created.Export(X509ContentType.Pfx);
        Directory.CreateDirectory(Settings.Directory);
        File.WriteAllBytes(path, pfx);
        // SChannel can't use the ephemeral key from CreateSelfSigned directly, so round-trip through PFX.
        return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        _certificate?.Dispose();
    }
}

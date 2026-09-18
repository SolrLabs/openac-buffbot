using System.Net.Sockets;
using System.Text;
using System.Threading;
using SolrLabs.BuffBot.Donations;

namespace SolrLabs.BuffBot.Web;

/// <summary>The hub's HTTP surface; a spoke never runs one of these. <see cref="Respond"/> and
/// everything it calls take a plain <see cref="Stream"/>, so routing is testable without a socket.</summary>
internal sealed class MeshServer
{
    private readonly TcpListener _listener;
    private readonly MeshRegistry _registry;
    private readonly Func<string> _currentKey;
    private readonly int _port;
    private readonly Func<string> _hubBotId;
    private readonly Action<MeshCommand> _localSink;
    private readonly byte[] _pageBytes;
    private readonly Action<string> _logInfo;
    private readonly Action<DateTimeOffset> _reportNodeStarted;
    private readonly Func<bool> _isDeciding;
    private readonly string _linkFilePath;
    private readonly Func<uint, IReadOnlyList<Contributor>>? _readContributors;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    internal MeshServer(
        TcpListener listener, MeshRegistry registry, Func<string> currentKey, int port, Func<string> hubBotId,
        Action<MeshCommand> localSink, byte[] pageBytes, Action<string> logInfo,
        Action<DateTimeOffset> reportNodeStarted, Func<bool> isDeciding, string linkFilePath,
        Func<uint, IReadOnlyList<Contributor>>? readContributors = null)
    {
        _listener = listener;
        _registry = registry;
        _currentKey = currentKey;
        _port = port;
        _hubBotId = hubBotId;
        _localSink = localSink;
        _pageBytes = pageBytes;
        _logInfo = logInfo;
        _reportNodeStarted = reportNodeStarted;
        _isDeciding = isDeciding;
        _linkFilePath = linkFilePath;
        _readContributors = readContributors;
    }

    internal void Start()
    {
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>Frees the port before doing anything else, so a caller depends on the port being free the instant this returns. Stopping the listener makes the pending accept fail immediately, so the short join below is a formality.</summary>
    internal void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
        }
        try
        {
            _acceptLoop?.Wait(TimeSpan.FromMilliseconds(60));
        }
        catch (AggregateException)
        {
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            using NetworkStream stream = client.GetStream();
            stream.ReadTimeout = 5000;
            stream.WriteTimeout = 5000;
            try
            {
                HttpParseResult parsed = await Task.Run(() => HttpRequestReader.Read(stream)).ConfigureAwait(false);
                if (parsed.Request is null)
                {
                    if (parsed.Status != HttpParseStatus.ConnectionClosed)
                        HttpResponseWriter.WriteEmpty(stream, 400, "Bad Request");
                    return;
                }

                Respond(stream, parsed.Request);
            }
            catch (IOException)
            {
                // Client timed out or reset mid-response.
            }
            catch (Exception ex)
            {
                _logInfo($"BuffBot web console request failed: {ex.Message}");
            }
        }
    }

    /// <summary>Every request, any path: Host and Origin checked first, regardless of what follows.</summary>
    internal void Respond(Stream stream, HttpRequest request)
    {
        if (!MeshAccessControl.HostAllowed(request.Header("Host"), _port)
            || !MeshAccessControl.OriginAllowed(request.Header("Origin"), _port))
        {
            HttpResponseWriter.WriteEmpty(stream, 403, "Forbidden");
            return;
        }

        if (request.Path == "/" && IsMethod(request, "GET"))
        {
            HttpResponseWriter.Write(
                stream, 200, "OK", "text/html; charset=utf-8", _pageBytes,
                new[] { ("Cache-Control", "no-store") });
            return;
        }

        if (request.Path == "/mesh/heartbeat" && IsMethod(request, "POST"))
        {
            RespondHeartbeat(stream, request);
            return;
        }

        // No token required — a user needs this to find the key before they have it.
        if (request.Path == "/api/hub" && IsMethod(request, "GET"))
        {
            RespondHub(stream);
            return;
        }

        if (request.Path.StartsWith("/api/", StringComparison.Ordinal))
        {
            if (!MeshAccessControl.TokenValid(request.Header("Authorization"), _currentKey()))
            {
                HttpResponseWriter.WriteEmpty(stream, 401, "Unauthorized");
                return;
            }
            RespondApi(stream, request);
            return;
        }

        HttpResponseWriter.WriteEmpty(stream, 404, "Not Found");
    }

    private void RespondHub(Stream stream)
    {
        string fingerprint = MeshKeyStore.Fingerprint(_currentKey());
        HttpResponseWriter.WriteJson(stream, 200, "OK", MeshJson.HubResponse(fingerprint, _isDeciding(), _linkFilePath));
    }

    private void RespondApi(Stream stream, HttpRequest request)
    {
        if (request.Path == "/api/bots" && IsMethod(request, "GET"))
        {
            HttpResponseWriter.WriteJson(stream, 200, "OK", MeshJson.BotsResponse(_registry.Snapshot()));
            return;
        }

        const string prefix = "/api/bots/";
        const string commandsSuffix = "/commands";
        // botId itself contains a slash (MeshIdentity.BotId is "world/objectId"), so the route is
        // matched by prefix/suffix rather than by splitting the path into fixed segments.
        if (request.Path.StartsWith(prefix, StringComparison.Ordinal)
            && request.Path.EndsWith(commandsSuffix, StringComparison.Ordinal)
            && request.Path.Length > prefix.Length + commandsSuffix.Length
            && IsMethod(request, "POST"))
        {
            string botId = Uri.UnescapeDataString(request.Path[prefix.Length..^commandsSuffix.Length]);
            RespondCommand(stream, request, botId);
            return;
        }

        const string contributorsSuffix = "/contributors";
        if (request.Path.StartsWith(prefix, StringComparison.Ordinal)
            && request.Path.EndsWith(contributorsSuffix, StringComparison.Ordinal)
            && request.Path.Length > prefix.Length + contributorsSuffix.Length
            && IsMethod(request, "GET"))
        {
            string botId = Uri.UnescapeDataString(request.Path[prefix.Length..^contributorsSuffix.Length]);
            RespondContributors(stream, botId);
            return;
        }

        HttpResponseWriter.WriteEmpty(stream, 404, "Not Found");
    }

    /// <summary>Every bot on this machine writes its ledger to the same plugin storage, keyed by
    /// character, so the hub answers for a spoke by reading that character's own file.</summary>
    private void RespondContributors(Stream stream, string botId)
    {
        int slash = botId.LastIndexOf('/');
        if (slash < 0 || !uint.TryParse(botId[(slash + 1)..], out uint characterObjectId))
        {
            HttpResponseWriter.WriteEmpty(stream, 404, "Not Found");
            return;
        }

        IReadOnlyList<Contributor> contributors =
            _readContributors?.Invoke(characterObjectId) ?? Array.Empty<Contributor>();
        HttpResponseWriter.WriteJson(stream, 200, "OK", MeshJson.ContributorsResponse(botId, contributors));
    }

    private void RespondCommand(Stream stream, HttpRequest request, string botId)
    {
        MeshCommand? command = MeshJson.TryParseCommand(Encoding.UTF8.GetString(request.Body));
        if (command is null)
        {
            HttpResponseWriter.WriteEmpty(stream, 400, "Bad Request");
            return;
        }

        if (botId == _hubBotId())
            _localSink(command);
        else if (_registry.Exists(botId))
            _registry.EnqueueCommand(botId, command);
        else
        {
            HttpResponseWriter.WriteEmpty(stream, 404, "Not Found");
            return;
        }

        HttpResponseWriter.WriteJson(stream, 202, "Accepted", MeshJson.Accepted());
    }

    private void RespondHeartbeat(Stream stream, HttpRequest request)
    {
        string key = _currentKey();
        if (!MeshAccessControl.TokenValid(request.Header("Authorization"), key))
        {
            HttpResponseWriter.WriteEmpty(stream, 401, "Unauthorized");
            return;
        }

        string? nonce = request.Header("X-BuffBot-Nonce");
        if (string.IsNullOrEmpty(nonce))
        {
            HttpResponseWriter.WriteEmpty(stream, 400, "Bad Request");
            return;
        }

        (string BotId, string Name, string World, MeshStatus Status, DateTimeOffset NodeStartedUtc)? heartbeat =
            MeshJson.TryParseHeartbeat(Encoding.UTF8.GetString(request.Body));
        if (heartbeat is not { } beat)
        {
            HttpResponseWriter.WriteEmpty(stream, 400, "Bad Request");
            return;
        }

        // A heartbeat presenting the current key is proof this node was already on the mesh before this hub bound; the caller decides what that means for its own grace window.
        _reportNodeStarted(beat.NodeStartedUtc);

        _registry.Report(beat.BotId, beat.Name, beat.World, isHub: false, beat.Status);
        IReadOnlyList<MeshCommand> commands = _registry.DrainCommands(beat.BotId);
        string proof = MeshProof.Compute(key, nonce);

        HttpResponseWriter.WriteJson(
            stream, 200, "OK", MeshJson.HeartbeatResponse(commands), new[] { ("X-BuffBot-Proof", proof) });
    }

    private static bool IsMethod(HttpRequest request, string method) =>
        string.Equals(request.Method, method, StringComparison.OrdinalIgnoreCase);
}

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using SolrLabs.BuffBot.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class MeshIntegrationTests : IDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan MaxJitter = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    private static readonly MeshStatus SampleStatus = new(
        Enabled: true, Activity: "idle", CurrentRequesterName: null, CurrentRequesterObjectId: null, CurrentSpellLine: null,
        CurrentSpellId: 0u, StepIndex: 0, StepCount: 0, CurrentMana: 0u, MaxMana: 0u,
        Waiting: [new MeshWaitingEntry(9, "Target", "heavy", 1d)], Counters: new MeshCounters(0, 0, 0, 0, 0), Stats: MeshStats.Empty, Recent: Array.Empty<MeshRecentEvent>());

    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "buffbot-mesh-integration-" + Guid.NewGuid());
    private readonly List<MeshNode> _nodes = [];
    private readonly HttpClient _http = new();

    [Fact]
    public async Task HubAndTwoSpokesRegisterCommandsDeliverAndTakeoverWorks()
    {
        int port = ReserveFreeLoopbackPort();

        MeshNode hub = NewNode(port);
        hub.Publish("local/1", "HubBot", "local", SampleStatus);
        hub.Start();
        Assert.True(hub.IsHub);

        MeshNode spokeA = NewNode(port);
        spokeA.Publish("local/2", "SpokeA", "local", SampleStatus);
        spokeA.Start();
        Assert.False(spokeA.IsHub);

        MeshNode spokeB = NewNode(port);
        spokeB.Publish("local/3", "SpokeB", "local", SampleStatus);
        spokeB.Start();
        Assert.False(spokeB.IsHub);

        // No credential at all -- the hub answers with no Authorization header present.
        using (HttpResponseMessage noAuth = await GetAsync(port, "/api/bots"))
            Assert.Equal(HttpStatusCode.OK, noAuth.StatusCode);
        using (HttpResponseMessage badHost = await GetAsync(port, "/api/bots", host: "example.com:1"))
            Assert.Equal(HttpStatusCode.Forbidden, badHost.StatusCode);

        JsonObject roster = await PollUntilAsync(port, static json => json["bots"]!.AsArray().Count == 3);
        Assert.Equal("local/1", roster["hubBotId"]!.GetValue<string>());

        // A command posted for a spoke reaches that spoke's own inbound queue.
        using (HttpResponseMessage posted = await PostCommandAsync(port, "local/2", "mute", 9u))
            Assert.Equal(HttpStatusCode.Accepted, posted.StatusCode);

        MeshCommand delivered = await PollForCommandAsync(spokeA);
        Assert.Equal(MeshCommandKind.Mute, delivered.Kind);
        Assert.Equal(9u, delivered.ObjectId);

        // Stopping the hub leads a spoke to take over, and /api/bots answers again.
        hub.Stop();
        JsonObject afterTakeover = await PollUntilAsync(
            port, static json => json["hubBotId"]!.GetValue<string>() != "local/1");
        Assert.True(spokeA.IsHub || spokeB.IsHub);
        Assert.Contains(afterTakeover["hubBotId"]!.GetValue<string>(), new[] { "local/2", "local/3" });
    }

    /// <summary>A reply that is not a BuffBot heartbeat response is ignored outright: never
    /// applied, and never treated as proof the hub is gone.</summary>
    [Fact]
    public async Task ASpokeIgnoresANonBuffBotReplyAndNeverTakesOver()
    {
        int port = ReserveFreeLoopbackPort();
        using var decoy = new DecoyHttpServer(port, "{\"unrelated\":true}");
        decoy.Start();

        MeshNode spoke = NewNode(port);
        spoke.Publish("local/2", "SpokeA", "local", SampleStatus);
        spoke.Start();
        Assert.False(spoke.IsHub);

        // Several heartbeat intervals pass with the decoy always answering 200 with junk.
        await Task.Delay(HeartbeatInterval * 6);

        Assert.False(spoke.IsHub);
        Assert.False(spoke.TryDequeueCommand(out _));
    }

    private MeshNode NewNode(int port)
    {
        var node = new MeshNode(new MeshNodeOptions(
            Port: port,
            LinkFilePath: Path.Combine(_tempDirectory, "opener.html"),
            Clock: SystemClock.Instance,
            PageBytes: "<html></html>"u8.ToArray(),
            LogInfo: static _ => { },
            HeartbeatInterval: HeartbeatInterval,
            MaxJitter: MaxJitter));
        _nodes.Add(node);
        return node;
    }

    private async Task<HttpResponseMessage> GetAsync(int port, string path, string? host = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}{path}");
        if (host is not null)
            request.Headers.Host = host;
        return await _http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostCommandAsync(int port, string botId, string kind, uint objectId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"http://127.0.0.1:{port}/api/bots/{Uri.EscapeDataString(botId)}/commands")
        {
            Content = new StringContent($"{{\"kind\":\"{kind}\",\"objectId\":{objectId}}}"),
        };
        return await _http.SendAsync(request);
    }

    private async Task<JsonObject> PollUntilAsync(int port, Func<JsonObject, bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using HttpResponseMessage response = await GetAsync(port, "/api/bots");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var json = (JsonObject)JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
                    if (predicate(json))
                        return json;
                }
            }
            catch (HttpRequestException)
            {
                // No hub answering this instant (e.g. mid-takeover) -- keep polling.
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("condition never became true within the poll timeout.");
    }

    private static async Task<MeshCommand> PollForCommandAsync(MeshNode node)
    {
        DateTime deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (node.TryDequeueCommand(out MeshCommand command))
                return command;
            await Task.Delay(20);
        }
        throw new TimeoutException("no command was delivered within the poll timeout.");
    }

    private static int ReserveFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose()
    {
        foreach (MeshNode node in _nodes)
            node.Dispose();
        _http.Dispose();
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    /// <summary>A bare listener standing in for "something else squatting on the port": answers
    /// every request with a fixed 200 OK body, never anything shaped like a BuffBot reply.</summary>
    private sealed class DecoyHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _body;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        internal DecoyHttpServer(int port, string body)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _body = body;
        }

        internal void Start()
        {
            _listener.Start();
            _loop = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }
                _ = HandleAsync(client);
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                var buffer = new byte[4096];
                try
                {
                    await stream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                byte[] bodyBytes = System.Text.Encoding.UTF8.GetBytes(_body);
                string head = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                try
                {
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(head), _cts.Token).ConfigureAwait(false);
                    await stream.WriteAsync(bodyBytes, _cts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
            }
            try
            {
                _loop?.Wait(TimeSpan.FromMilliseconds(200));
            }
            catch (AggregateException)
            {
            }
        }
    }
}

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string key = MeshKeyStore.EnsureKey(keyPath);

        MeshNode hub = NewNode(port, keyPath);
        hub.Publish("local/1", "HubBot", "local", SampleStatus);
        hub.Start();
        Assert.True(hub.IsHub);

        MeshNode spokeA = NewNode(port, keyPath);
        spokeA.Publish("local/2", "SpokeA", "local", SampleStatus);
        spokeA.Start();
        Assert.False(spokeA.IsHub);

        MeshNode spokeB = NewNode(port, keyPath);
        spokeB.Publish("local/3", "SpokeB", "local", SampleStatus);
        spokeB.Start();
        Assert.False(spokeB.IsHub);

        // Wrong token / bad Host, answered by the hub before anything else happens.
        using (HttpResponseMessage wrongToken = await GetAsync(port, "/api/bots", key: "not-the-key"))
            Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
        using (HttpResponseMessage badHost = await GetAsync(port, "/api/bots", key, host: "example.com:1"))
            Assert.Equal(HttpStatusCode.Forbidden, badHost.StatusCode);

        JsonObject roster = await PollUntilAsync(port, key, static json => json["bots"]!.AsArray().Count == 3);
        Assert.Equal("local/1", roster["hubBotId"]!.GetValue<string>());

        // A command posted for a spoke reaches that spoke's own inbound queue with a verified proof.
        using (HttpResponseMessage posted = await PostCommandAsync(port, key, "local/2", "mute", 9u))
            Assert.Equal(HttpStatusCode.Accepted, posted.StatusCode);

        MeshCommand delivered = await PollForCommandAsync(spokeA);
        Assert.Equal(MeshCommandKind.Mute, delivered.Kind);
        Assert.Equal(9u, delivered.ObjectId);

        // Stopping the hub leads a spoke to take over, and /api/bots answers again.
        hub.Stop();
        JsonObject afterTakeover = await PollUntilAsync(
            port, key, static json => json["hubBotId"]!.GetValue<string>() != "local/1");
        Assert.True(spokeA.IsHub || spokeB.IsHub);
        Assert.Contains(afterTakeover["hubBotId"]!.GetValue<string>(), new[] { "local/2", "local/3" });
    }

    private MeshNode NewNode(int port, string keyPath)
    {
        var node = new MeshNode(new MeshNodeOptions(
            Port: port,
            KeyPath: keyPath,
            LinkFilePath: Path.Combine(_tempDirectory, "opener.html"),
            Clock: SystemClock.Instance,
            PageBytes: "<html></html>"u8.ToArray(),
            LogInfo: static _ => { },
            HeartbeatInterval: HeartbeatInterval,
            MaxJitter: MaxJitter));
        _nodes.Add(node);
        return node;
    }

    private async Task<HttpResponseMessage> GetAsync(int port, string path, string key, string? host = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (host is not null)
            request.Headers.Host = host;
        return await _http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostCommandAsync(int port, string key, string botId, string kind, uint objectId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"http://127.0.0.1:{port}/api/bots/{Uri.EscapeDataString(botId)}/commands")
        {
            Content = new StringContent($"{{\"kind\":\"{kind}\",\"objectId\":{objectId}}}"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return await _http.SendAsync(request);
    }

    private async Task<JsonObject> PollUntilAsync(int port, string key, Func<JsonObject, bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using HttpResponseMessage response = await GetAsync(port, "/api/bots", key);
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
}

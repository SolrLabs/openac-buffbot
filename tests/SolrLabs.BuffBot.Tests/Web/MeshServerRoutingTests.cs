using System.Text;
using SolrLabs.BuffBot.Donations;
using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class MeshServerRoutingTests
{
    private const int Port = 8347;
    private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string HubBotId = "local/1";
    private const string SpokeBotId = "world/2";

    private static readonly MeshStatus SampleStatus = new(
        Enabled: true, Activity: "idle", CurrentRequesterName: null, CurrentRequesterObjectId: null, CurrentSpellLine: null,
        CurrentSpellId: 0u, StepIndex: 0, StepCount: 0, CurrentMana: 0u, MaxMana: 0u,
        Waiting: [], Counters: new MeshCounters(0, 0, 0, 0, 0), Stats: MeshStats.Empty, Recent: Array.Empty<MeshRecentEvent>());

    [Fact]
    public void GetApiBotsReturnsTheRegisteredBots()
    {
        var (server, _, _) = NewServer();

        (int status, string body) = Send(server, Get("/api/bots"));

        Assert.Equal(200, status);
        Assert.Contains(HubBotId, body);
        Assert.Contains(SpokeBotId, body);
    }

    [Fact]
    public void GetApiBotsWithoutABearerTokenIsUnauthorized()
    {
        var (server, _, _) = NewServer();

        (int status, _) = Send(server, Get("/api/bots", key: null));

        Assert.Equal(401, status);
    }

    [Fact]
    public void AnyRouteWithAWrongHostIsForbidden()
    {
        var (server, _, _) = NewServer();

        (int status, _) = Send(server, Get("/api/bots", host: "example.com:1"));

        Assert.Equal(403, status);
    }

    [Fact]
    public void AnyRouteWithAWrongOriginIsForbidden()
    {
        var (server, _, _) = NewServer();

        (int status, _) = Send(server, Get("/api/bots", origin: "http://evil.example:8347"));

        Assert.Equal(403, status);
    }

    [Fact]
    public void ACommandForTheHubsOwnBotGoesStraightToTheLocalSinkAndReturns202()
    {
        var (server, _, sinkCommands) = NewServer();

        (int status, string body) = Send(server, PostCommand(HubBotId, "{\"kind\":\"unwedge\"}"));

        Assert.Equal(202, status);
        Assert.Equal("{\"accepted\":true}", body);
        Assert.Equal(MeshCommandKind.Unwedge, Assert.Single(sinkCommands).Kind);
    }

    [Fact]
    public void ACommandForAKnownSpokeIsQueuedRatherThanSentToTheLocalSink()
    {
        var (server, registry, sinkCommands) = NewServer();

        (int status, _) = Send(server, PostCommand(SpokeBotId, "{\"kind\":\"mute\",\"objectId\":5}"));

        Assert.Equal(202, status);
        Assert.Empty(sinkCommands);
        Assert.Single(registry.DrainCommands(SpokeBotId));
    }

    [Fact]
    public void ACommandForAnUnknownBotIsNotFound()
    {
        var (server, _, _) = NewServer();

        (int status, _) = Send(server, PostCommand("nobody/9", "{\"kind\":\"unwedge\"}"));

        Assert.Equal(404, status);
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    public void AnEnableOrDisableCommandForTheHubsOwnBotGoesStraightToTheLocalSink(string wireKind)
    {
        MeshCommandKind expectedKind = wireKind == "enable" ? MeshCommandKind.Enable : MeshCommandKind.Disable;
        var (server, _, sinkCommands) = NewServer();

        (int status, string body) = Send(server, PostCommand(HubBotId, $"{{\"kind\":\"{wireKind}\"}}"));

        Assert.Equal(202, status);
        Assert.Equal("{\"accepted\":true}", body);
        Assert.Equal(expectedKind, Assert.Single(sinkCommands).Kind);
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    public void AnEnableOrDisableCommandForAKnownSpokeIsQueued(string wireKind)
    {
        var (server, registry, sinkCommands) = NewServer();

        (int status, _) = Send(server, PostCommand(SpokeBotId, $"{{\"kind\":\"{wireKind}\"}}"));

        Assert.Equal(202, status);
        Assert.Empty(sinkCommands);
        Assert.Single(registry.DrainCommands(SpokeBotId));
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    public void AnEnableOrDisableCommandForAnUnknownBotIsNotFound(string wireKind)
    {
        var (server, _, _) = NewServer();

        (int status, _) = Send(server, PostCommand("nobody/9", $"{{\"kind\":\"{wireKind}\"}}"));

        Assert.Equal(404, status);
    }

    /// <summary>A bot aged out of the registry is a 404, like any unknown botId.</summary>
    [Fact]
    public void ACommandForABotTheRegistryHasRemovedIsNotFound()
    {
        var clock = new FakeClock();
        var registry = new MeshRegistry(clock, staleAfter: TimeSpan.FromSeconds(6), removeAfter: TimeSpan.FromSeconds(120));
        registry.Report(HubBotId, "HubBot", "local", isHub: true, SampleStatus);
        registry.Report(SpokeBotId, "SpokeBot", "world", isHub: false, SampleStatus);
        clock.Advance(TimeSpan.FromSeconds(121));
        registry.Snapshot(); // removal is computed lazily, on read

        var sinkCommands = new List<MeshCommand>();
        var server = new MeshServer(
            listener: null!, registry, currentKey: () => Key, Port, hubBotId: () => HubBotId, localSink: sinkCommands.Add,
            pageBytes: [], logInfo: static _ => { }, reportNodeStarted: static _ => { }, isDeciding: static () => false,
            linkFilePath: "unused-in-these-tests.html");

        (int status, _) = Send(server, PostCommand(SpokeBotId, "{\"kind\":\"mute\",\"objectId\":5}"));

        Assert.Equal(404, status);
    }

    [Theory]
    [InlineData("{\"kind\":\"mute\"}")]
    [InlineData("{\"kind\":\"nonsense\"}")]
    [InlineData("not json")]
    public void AMalformedCommandBodyIsABadRequest(string body)
    {
        var (server, _, _) = NewServer();

        (int status, _) = Send(server, PostCommand(HubBotId, body));

        Assert.Equal(400, status);
    }

    [Fact]
    public void AnUnknownRouteUnderApiIsNotFound()
    {
        var (server, _, _) = NewServer();

        (int status, _) = Send(server, Get("/api/nonsense"));

        Assert.Equal(404, status);
    }

    /// <summary>The hub's own bot answers straight from its own ledger reader.</summary>
    [Fact]
    public void GetContributorsForTheHubsOwnBotReturnsThePopulatedLedgerNewestFirst()
    {
        var earlier = new ContributorEntry("Lead Scarab", 691u, 5, new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc));
        var later = new ContributorEntry("Prismatic Pea", 1234u, 1, new DateTime(2026, 9, 17, 11, 42, 0, DateTimeKind.Utc));
        var contributors = new List<Contributor> { new(DonorObjectId: 5u, DonorName: "Archer", Entries: [earlier, later]) };
        var (server, _, _) = NewServer(readContributors: _ => contributors);

        (int status, string body) = Send(server, Get($"/api/bots/{Uri.EscapeDataString(HubBotId)}/contributors"));

        Assert.Equal(200, status);
        Assert.Contains("\"botId\":\"local/1\"", body);
        Assert.Contains("\"donorObjectId\":5", body);
        Assert.Contains("\"donorName\":\"Archer\"", body);
        Assert.Contains("\"totalCount\":6", body);
        // Newest entry first within a donor, regardless of append order.
        int laterIndex = body.IndexOf("Prismatic Pea", StringComparison.Ordinal);
        int earlierIndex = body.IndexOf("Lead Scarab", StringComparison.Ordinal);
        Assert.True(laterIndex >= 0 && earlierIndex >= 0 && laterIndex < earlierIndex);
    }

    [Fact]
    public void GetContributorsForTheHubsOwnBotWithAnEmptyLedgerReturnsAnEmptyArray()
    {
        var (server, _, _) = NewServer(readContributors: _ => Array.Empty<Contributor>());

        (int status, string body) = Send(server, Get($"/api/bots/{Uri.EscapeDataString(HubBotId)}/contributors"));

        Assert.Equal(200, status);
        Assert.Contains("\"contributors\":[]", body);
    }

    /// <summary>Every bot on the machine shares one plugin storage, so the hub reads a spoke's
    /// ledger by that spoke's own character id.</summary>
    [Fact]
    public void GetContributorsForAKnownSpokeReadsThatCharactersLedger()
    {
        var asked = new List<uint>();
        var (server, _, _) = NewServer(readContributors: id =>
        {
            asked.Add(id);
            return Array.Empty<Contributor>();
        });

        (int status, _) = Send(server, Get($"/api/bots/{Uri.EscapeDataString(SpokeBotId)}/contributors"));

        Assert.Equal(200, status);
        Assert.Equal(uint.Parse(SpokeBotId[(SpokeBotId.LastIndexOf('/') + 1)..]), Assert.Single(asked));
    }

    /// <summary>A botId whose tail is not a character id has no ledger to read.</summary>
    [Fact]
    public void GetContributorsForABotIdWithoutACharacterIdIsNotFound()
    {
        var (server, _, _) = NewServer();

        (int status, _) = Send(server, Get("/api/bots/nobody%2Fnotanid/contributors"));

        Assert.Equal(404, status);
    }

    [Fact]
    public void GetContributorsWithoutABearerTokenIsUnauthorized()
    {
        var (server, _, _) = NewServer();

        (int status, _) = Send(server, Get($"/api/bots/{Uri.EscapeDataString(HubBotId)}/contributors", key: null));

        Assert.Equal(401, status);
    }

    private static (MeshServer Server, MeshRegistry Registry, List<MeshCommand> SinkCommands) NewServer(
        Func<uint, IReadOnlyList<Contributor>>? readContributors = null)
    {
        var registry = new MeshRegistry(new FakeClock());
        registry.Report(HubBotId, "HubBot", "local", isHub: true, SampleStatus);
        registry.Report(SpokeBotId, "SpokeBot", "world", isHub: false, SampleStatus);

        var sinkCommands = new List<MeshCommand>();
        var server = new MeshServer(
            listener: null!, registry, currentKey: () => Key, Port, hubBotId: () => HubBotId, localSink: sinkCommands.Add,
            pageBytes: [], logInfo: static _ => { }, reportNodeStarted: static _ => { }, isDeciding: static () => false,
            linkFilePath: "unused-in-these-tests.html", readContributors: readContributors);
        return (server, registry, sinkCommands);
    }

    private static HttpRequest Get(string path, string? key = Key, string? host = null, string? origin = null) => new(
        "GET", path, path,
        Headers(key, host, origin), Array.Empty<byte>());

    private static HttpRequest PostCommand(string botId, string body) => new(
        "POST", $"/api/bots/{Uri.EscapeDataString(botId)}/commands", "",
        Headers(Key, null, null), Encoding.UTF8.GetBytes(body));

    private static Dictionary<string, string> Headers(string? key, string? host, string? origin)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Host"] = host ?? $"127.0.0.1:{Port}",
        };
        if (origin is not null)
            headers["Origin"] = origin;
        if (key is not null)
            headers["Authorization"] = $"Bearer {key}";
        return headers;
    }

    private static (int Status, string Body) Send(MeshServer server, HttpRequest request)
    {
        using var stream = new MemoryStream();
        server.Respond(stream, request);
        stream.Position = 0;
        string raw = Encoding.ASCII.GetString(stream.ToArray());
        int statusStart = raw.IndexOf(' ') + 1;
        int status = int.Parse(raw.Substring(statusStart, 3));
        string body = raw[(raw.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..];
        return (status, body);
    }
}

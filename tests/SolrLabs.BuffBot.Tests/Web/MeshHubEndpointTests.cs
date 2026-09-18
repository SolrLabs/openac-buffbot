using System.Text;
using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class MeshHubEndpointTests
{
    private const int Port = 8347;
    private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string LinkFilePath = "/tmp/does-not-matter/Open BuffBot console.html";

    [Fact]
    public void AnswersWithNoTokenAtAll()
    {
        MeshServer server = NewServer(isDeciding: false);

        (int status, _) = Send(server, Get(key: null));

        Assert.Equal(200, status);
    }

    [Fact]
    public void FingerprintMatchesTheSpecifiedShaTwoFiftySixPrefix()
    {
        MeshServer server = NewServer(isDeciding: false);

        (_, string body) = Send(server, Get(key: null));

        string expected = MeshKeyStore.Fingerprint(Key);
        Assert.Contains($"\"keyFingerprint\":\"{expected}\"", body);
        Assert.Equal(16, expected.Length);
        Assert.DoesNotContain(Key, body);
    }

    [Fact]
    public void DecidingIsTrueWhileTheStartupGraceWindowIsOpen()
    {
        MeshServer server = NewServer(isDeciding: true);

        (_, string body) = Send(server, Get(key: null));

        Assert.Contains("\"deciding\":true", body);
    }

    [Fact]
    public void DecidingIsFalseOnceTheHubHasDecided()
    {
        MeshServer server = NewServer(isDeciding: false);

        (_, string body) = Send(server, Get(key: null));

        Assert.Contains("\"deciding\":false", body);
    }

    [Fact]
    public void ReportsTheOpenerFilesAbsolutePath()
    {
        MeshServer server = NewServer(isDeciding: false);

        (_, string body) = Send(server, Get(key: null));

        Assert.Contains(LinkFilePath, body);
    }

    [Fact]
    public void AWrongHostIsStillForbiddenWithNoToken()
    {
        MeshServer server = NewServer(isDeciding: false);

        (int status, _) = Send(server, Get(key: null, host: "example.com:1"));

        Assert.Equal(403, status);
    }

    private static MeshServer NewServer(bool isDeciding) => new(
        listener: null!, new MeshRegistry(new FakeClock()), currentKey: () => Key, Port,
        hubBotId: () => "local/1", localSink: static _ => { }, pageBytes: [], logInfo: static _ => { },
        reportNodeStarted: static _ => { }, isDeciding: () => isDeciding, linkFilePath: LinkFilePath);

    private static HttpRequest Get(string? key, string? host = null) => new(
        "GET", "/api/hub", "/api/hub", Headers(key, host), Array.Empty<byte>());

    private static Dictionary<string, string> Headers(string? key, string? host)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Host"] = host ?? $"127.0.0.1:{Port}",
        };
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

using System.Text;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class HttpRequestReaderTests
{
    [Fact]
    public void ParsesAnOrdinaryGetRequest()
    {
        HttpParseResult result = Read("GET /api/bots?x=1 HTTP/1.1\r\nHost: 127.0.0.1:8347\r\n\r\n");

        Assert.Equal(HttpParseStatus.Ok, result.Status);
        Assert.Equal("GET", result.Request!.Method);
        Assert.Equal("/api/bots", result.Request.Path);
        Assert.Equal("127.0.0.1:8347", result.Request.Header("Host"));
        Assert.Empty(result.Request.Body);
    }

    [Fact]
    public void ParsesAPostRequestWithABody()
    {
        const string body = "{\"kind\":\"unwedge\"}";
        HttpParseResult result = Read(
            "POST /api/bots/local%2F1/commands HTTP/1.1\r\n"
            + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}");

        Assert.Equal(HttpParseStatus.Ok, result.Status);
        Assert.Equal("POST", result.Request!.Method);
        Assert.Equal(body, Encoding.UTF8.GetString(result.Request.Body));
    }

    [Fact]
    public void HeaderNamesAreCaseInsensitive()
    {
        HttpParseResult result = Read("GET / HTTP/1.1\r\nhost: 127.0.0.1:8347\r\n\r\n");

        Assert.Equal("127.0.0.1:8347", result.Request!.Header("Host"));
    }

    [Fact]
    public void RejectsAHeaderBlockOverEightKilobytes()
    {
        string hugeHeader = "X-Padding: " + new string('a', HttpRequestReader.MaxHeaderBytes) + "\r\n";
        HttpParseResult result = Read($"GET / HTTP/1.1\r\n{hugeHeader}\r\n");

        Assert.Equal(HttpParseStatus.HeaderBlockTooLarge, result.Status);
        Assert.Null(result.Request);
    }

    [Fact]
    public void RejectsAPostWithNoContentLength()
    {
        HttpParseResult result = Read("POST /mesh/heartbeat HTTP/1.1\r\n\r\n");

        Assert.Equal(HttpParseStatus.MissingContentLength, result.Status);
    }

    [Fact]
    public void RejectsAMalformedRequestLine()
    {
        HttpParseResult result = Read("NOT A REQUEST LINE\r\n\r\n");

        Assert.Equal(HttpParseStatus.BadRequestLine, result.Status);
    }

    [Fact]
    public void RejectsARequestLineMissingTheHttpVersion()
    {
        HttpParseResult result = Read("GET /\r\n\r\n");

        Assert.Equal(HttpParseStatus.BadRequestLine, result.Status);
    }

    [Fact]
    public void RejectsABodyOverTheSixtyFourKilobyteLimit()
    {
        int tooLarge = HttpRequestReader.MaxBodyBytes + 1;
        HttpParseResult result = Read($"POST / HTTP/1.1\r\nContent-Length: {tooLarge}\r\n\r\n");

        Assert.Equal(HttpParseStatus.BodyTooLarge, result.Status);
    }

    [Fact]
    public void RejectsChunkedTransferEncoding()
    {
        HttpParseResult result = Read("POST / HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n1\r\nx\r\n0\r\n\r\n");

        Assert.Equal(HttpParseStatus.ChunkedNotSupported, result.Status);
    }

    [Fact]
    public void RejectsAMalformedHeaderLine()
    {
        HttpParseResult result = Read("GET / HTTP/1.1\r\nNotAHeader\r\n\r\n");

        Assert.Equal(HttpParseStatus.MalformedHeader, result.Status);
    }

    [Fact]
    public void RejectsANonNumericContentLength()
    {
        HttpParseResult result = Read("POST / HTTP/1.1\r\nContent-Length: banana\r\n\r\n");

        Assert.Equal(HttpParseStatus.InvalidContentLength, result.Status);
    }

    [Fact]
    public void ReportsAConnectionClosedBeforeAnyByteArrived()
    {
        HttpParseResult result = HttpRequestReader.Read(new MemoryStream());

        Assert.Equal(HttpParseStatus.ConnectionClosed, result.Status);
        Assert.Null(result.Request);
    }

    [Fact]
    public void ReportsAnIncompleteBodyWhenTheStreamEndsEarly()
    {
        Stream stream = ToStream("POST / HTTP/1.1\r\nContent-Length: 10\r\n\r\nabc");

        HttpParseResult result = HttpRequestReader.Read(stream);

        Assert.Equal(HttpParseStatus.IncompleteBody, result.Status);
    }

    private static HttpParseResult Read(string raw) => HttpRequestReader.Read(ToStream(raw));

    private static Stream ToStream(string raw) => new MemoryStream(Encoding.ASCII.GetBytes(raw));
}

namespace SolrLabs.BuffBot.Web;

/// <summary><see cref="Path"/> has the query string already stripped, since every route match in <see cref="MeshServer"/> is against the path alone.</summary>
internal sealed record HttpRequest(
    string Method, string Path, string RawTarget, IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    internal string? Header(string name) => Headers.TryGetValue(name, out string? value) ? value : null;
}

/// <summary>Every way <see cref="HttpRequestReader.Read"/> can fail to produce a request, so the caller can answer with a real status code instead of tearing the connection down blind. <see cref="ConnectionClosed"/> is the one case with nothing to answer — the peer went away before sending a byte.</summary>
internal enum HttpParseStatus
{
    Ok,
    ConnectionClosed,
    HeaderBlockTooLarge,
    BadRequestLine,
    MalformedHeader,
    MissingContentLength,
    InvalidContentLength,
    ChunkedNotSupported,
    BodyTooLarge,
    IncompleteBody,
}

internal readonly record struct HttpParseResult(HttpParseStatus Status, HttpRequest? Request)
{
    internal static HttpParseResult Failure(HttpParseStatus status) => new(status, null);

    internal static HttpParseResult Success(HttpRequest request) => new(HttpParseStatus.Ok, request);
}

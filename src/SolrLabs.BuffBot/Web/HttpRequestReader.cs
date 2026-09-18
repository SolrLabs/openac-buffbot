using System.Text;

namespace SolrLabs.BuffBot.Web;

/// <summary>A from-scratch HTTP/1.1 request reader over a plain <see cref="Stream"/> — deliberately not <see cref="System.Net.HttpListener"/>, whose http.sys URL-ACL and prefix-reservation rules on Windows do not behave the way a loopback <see cref="System.Net.Sockets.TcpListener"/> does. One request per connection; the read timeout lives on the caller's stream, not here, so this same reader runs unmodified over the <see cref="MemoryStream"/>s the tests use.</summary>
internal static class HttpRequestReader
{
    internal const int MaxHeaderBytes = 8 * 1024;
    internal const int MaxBodyBytes = 64 * 1024;

    internal static HttpParseResult Read(Stream stream)
    {
        HttpParseStatus headerStatus = ReadHeaderBlock(stream, out byte[] headerBlock);
        if (headerStatus != HttpParseStatus.Ok)
            return HttpParseResult.Failure(headerStatus);

        // headerBlock ends with the terminating blank line's own CRLFCRLF; drop it before
        // splitting so there is no trailing empty "line" to special-case.
        string text = Encoding.ASCII.GetString(headerBlock);
        string[] lines = text[..^4].Split("\r\n");
        if (lines.Length == 0 || lines[0].Length == 0)
            return HttpParseResult.Failure(HttpParseStatus.BadRequestLine);

        string[] requestLine = lines[0].Split(' ');
        if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            return HttpParseResult.Failure(HttpParseStatus.BadRequestLine);

        string method = requestLine[0];
        string rawTarget = requestLine[1];
        string path = rawTarget.Split('?', 2)[0];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0)
                return HttpParseResult.Failure(HttpParseStatus.MalformedHeader);
            headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        if (headers.ContainsKey("Transfer-Encoding"))
            return HttpParseResult.Failure(HttpParseStatus.ChunkedNotSupported);

        bool hasContentLength = headers.TryGetValue("Content-Length", out string? lengthText);
        if (!hasContentLength)
        {
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                return HttpParseResult.Failure(HttpParseStatus.MissingContentLength);

            return HttpParseResult.Success(new HttpRequest(method, path, rawTarget, headers, Array.Empty<byte>()));
        }

        if (!int.TryParse(lengthText, out int contentLength) || contentLength < 0)
            return HttpParseResult.Failure(HttpParseStatus.InvalidContentLength);
        if (contentLength > MaxBodyBytes)
            return HttpParseResult.Failure(HttpParseStatus.BodyTooLarge);

        byte[] body = new byte[contentLength];
        int totalRead = 0;
        while (totalRead < contentLength)
        {
            int read = stream.Read(body, totalRead, contentLength - totalRead);
            if (read == 0)
                return HttpParseResult.Failure(HttpParseStatus.IncompleteBody);
            totalRead += read;
        }

        return HttpParseResult.Success(new HttpRequest(method, path, rawTarget, headers, body));
    }

    private static HttpParseStatus ReadHeaderBlock(Stream stream, out byte[] block)
    {
        var bytes = new List<byte>(256);
        byte[] one = new byte[1];
        int state = 0; // walks the \r\n\r\n state machine below one byte at a time
        while (true)
        {
            int read = stream.Read(one, 0, 1);
            if (read == 0)
            {
                block = Array.Empty<byte>();
                return bytes.Count == 0 ? HttpParseStatus.ConnectionClosed : HttpParseStatus.BadRequestLine;
            }

            bytes.Add(one[0]);
            if (bytes.Count > MaxHeaderBytes)
            {
                block = Array.Empty<byte>();
                return HttpParseStatus.HeaderBlockTooLarge;
            }

            state = one[0] switch
            {
                (byte)'\r' when state is 0 or 2 => state + 1,
                (byte)'\n' when state is 1 or 3 => state + 1,
                _ => 0,
            };
            if (state == 4)
            {
                block = bytes.ToArray();
                return HttpParseStatus.Ok;
            }
        }
    }
}

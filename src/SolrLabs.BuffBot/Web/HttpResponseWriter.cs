using System.Text;

namespace SolrLabs.BuffBot.Web;

/// <summary>Every response this server sends carries <c>Connection: close</c>, so the caller never has to remember it.</summary>
internal static class HttpResponseWriter
{
    internal static void Write(
        Stream stream, int statusCode, string reason, string contentType, byte[] body,
        IReadOnlyList<(string Name, string Value)>? extraHeaders = null)
    {
        var head = new StringBuilder(128);
        head.Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(reason).Append("\r\n");
        head.Append("Content-Type: ").Append(contentType).Append("\r\n");
        head.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        head.Append("Connection: close\r\n");
        if (extraHeaders is not null)
            foreach ((string name, string value) in extraHeaders)
                head.Append(name).Append(": ").Append(value).Append("\r\n");
        head.Append("\r\n");

        stream.Write(Encoding.ASCII.GetBytes(head.ToString()));
        if (body.Length > 0)
            stream.Write(body);
        stream.Flush();
    }

    internal static void WriteJson(
        Stream stream, int statusCode, string reason, string json,
        IReadOnlyList<(string Name, string Value)>? extraHeaders = null) => Write(
        stream, statusCode, reason, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), extraHeaders);

    internal static void WriteEmpty(Stream stream, int statusCode, string reason) =>
        Write(stream, statusCode, reason, "text/plain; charset=utf-8", Array.Empty<byte>());
}

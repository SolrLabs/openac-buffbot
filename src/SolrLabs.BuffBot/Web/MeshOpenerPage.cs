using System.Text;

namespace SolrLabs.BuffBot.Web;

/// <summary>The file a user without a terminal can double-click: a meta-refresh redirect to the tokenized console URL, plus a plain link for a browser that ignores the refresh. Rewritten atomically every time <see cref="MeshNode"/> decides a key.</summary>
internal static class MeshOpenerPage
{
    internal static string DefaultPath() => Path.Combine(MeshKeyStore.BaseDirectory(), "Open BuffBot console.html");

    internal static void Write(string path, string url)
    {
        string html = $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="refresh" content="0; url={url}">
            <title>BuffBot web console</title>
            </head>
            <body>
            <p><a href="{url}">Open the BuffBot web console</a></p>
            </body>
            </html>
            """;
        AtomicFile.Write(path, Encoding.UTF8.GetBytes(html));
    }
}

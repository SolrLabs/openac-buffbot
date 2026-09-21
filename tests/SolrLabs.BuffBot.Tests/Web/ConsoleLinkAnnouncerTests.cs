using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class ConsoleLinkAnnouncerTests
{
    [Fact]
    public void TheFirstLinkPosts()
    {
        var announcer = new ConsoleLinkAnnouncer();

        string? line = announcer.Observe("http://127.0.0.1:8347/?token=abc", hasUi: false);

        Assert.Equal("BuffBot web console: http://127.0.0.1:8347/?token=abc", line);
    }

    [Fact]
    public void TheSameLinkAgainDoesNotPostTwice()
    {
        var announcer = new ConsoleLinkAnnouncer();
        announcer.Observe("http://127.0.0.1:8347/?token=abc", hasUi: false);

        string? line = announcer.Observe("http://127.0.0.1:8347/?token=abc", hasUi: false);

        Assert.Null(line);
    }

    [Fact]
    public void AChangedLinkPostsAgain()
    {
        var announcer = new ConsoleLinkAnnouncer();
        announcer.Observe("http://127.0.0.1:8347/?token=abc", hasUi: false);

        string? line = announcer.Observe("http://127.0.0.1:8347/?token=xyz", hasUi: false);

        Assert.Equal("BuffBot web console: http://127.0.0.1:8347/?token=xyz", line);
    }

    [Fact]
    public void AHasUiHostNeverAutoPosts()
    {
        var announcer = new ConsoleLinkAnnouncer();

        string? line = announcer.Observe("http://127.0.0.1:8347/?token=abc", hasUi: true);

        Assert.Null(line);
    }

    [Fact]
    public void ANullOrEmptyLinkNeverPosts()
    {
        var announcer = new ConsoleLinkAnnouncer();

        Assert.Null(announcer.Observe(null, hasUi: false));
        Assert.Null(announcer.Observe(string.Empty, hasUi: false));
    }
}

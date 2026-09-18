using System.Text;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class ConsolePageStartupOrderTests
{
    [Fact]
    public void BuffBotSettingsDefaultsIsAssignedBeforeItsFirstRead()
    {
        string html = Encoding.UTF8.GetString(ConsolePage.Load());

        int assignmentIndex = html.IndexOf("var BuffBotSettingsDefaults =", StringComparison.Ordinal);
        int firstReadIndex = html.IndexOf("BuffBotSettingsDefaults.", StringComparison.Ordinal);

        Assert.True(assignmentIndex >= 0, "the page no longer declares BuffBotSettingsDefaults at all");
        Assert.True(firstReadIndex >= 0, "the page no longer reads BuffBotSettingsDefaults at all");
        Assert.True(
            assignmentIndex < firstReadIndex,
            "BuffBotSettingsDefaults is read before it is assigned -- the hoisted `undefined` "
            + "throws and kills the whole script before initToken/poll ever run");
    }
}

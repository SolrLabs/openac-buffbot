using System.Text;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class ConsoleMarkupTests
{
    private static string Html() => Encoding.UTF8.GetString(ConsolePage.Load());

    // -- tabs ---------------------------------------------------------------------------------

    [Fact]
    public void ThreeTabsExistWithTheRightRolesAndOrder()
    {
        string html = Html();

        int tablist = html.IndexOf("role=\"tablist\"", StringComparison.Ordinal);
        int tabLive = html.IndexOf("id=\"tab-live\"", StringComparison.Ordinal);
        int tabComponents = html.IndexOf("id=\"tab-components\"", StringComparison.Ordinal);
        int tabSettings = html.IndexOf("id=\"tab-settings\"", StringComparison.Ordinal);

        Assert.True(tablist >= 0, "no role=\"tablist\" container");
        Assert.True(tabLive >= 0 && tabComponents >= 0 && tabSettings >= 0, "not all three tabs exist");
        Assert.True(tablist < tabLive && tabLive < tabComponents && tabComponents < tabSettings,
            "tabs are not Live, Components, Settings in that order");

        Assert.Contains("role=\"tab\" id=\"tab-live\"", html);
        Assert.Contains("role=\"tab\" id=\"tab-components\"", html);
        Assert.Contains("role=\"tab\" id=\"tab-settings\"", html);
    }

    [Fact]
    public void LiveTabIsSelectedByDefaultAndTheOthersAreNot()
    {
        string html = Html();

        Assert.Contains("id=\"tab-live\" aria-controls=\"panel-live\" aria-selected=\"true\"", html);
        Assert.Contains(
            "id=\"tab-components\" aria-controls=\"panel-components\" aria-selected=\"false\"", html);
        Assert.Contains(
            "id=\"tab-settings\" aria-controls=\"panel-settings\" aria-selected=\"false\"", html);
    }

    [Fact]
    public void EachTabPanelHasTheTabpanelRoleAndOnlyLiveStartsUnhidden()
    {
        string html = Html();

        Assert.Contains("id=\"panel-live\" role=\"tabpanel\" aria-labelledby=\"tab-live\"", html);
        Assert.Contains(
            "id=\"panel-components\" role=\"tabpanel\" aria-labelledby=\"tab-components\" tabindex=\"0\" hidden",
            html);
        Assert.Contains(
            "id=\"panel-settings\" role=\"tabpanel\" aria-labelledby=\"tab-settings\" tabindex=\"0\" hidden",
            html);
    }

    [Fact]
    public void TheComponentsPanelHoldsOnlyTheComponentsSectionAndTheSettingsPanelOnlyControls()
    {
        string html = Html();

        int panelComponents = html.IndexOf("id=\"panel-components\"", StringComparison.Ordinal);
        int panelSettings = html.IndexOf("id=\"panel-settings\"", StringComparison.Ordinal);
        string componentsPanel = html[panelComponents..panelSettings];

        Assert.Contains("id=\"comps\"", componentsPanel);
        Assert.DoesNotContain("id=\"target-tier\"", componentsPanel);

        int panelSettingsEnd = html.IndexOf("<footer>", panelSettings, StringComparison.Ordinal);
        string settingsPanel = html[panelSettings..panelSettingsEnd];
        Assert.Contains("id=\"target-tier\"", settingsPanel);
        Assert.DoesNotContain("id=\"comps\"", settingsPanel);
    }

    [Fact]
    public void TabsRespondToArrowKeysAndPersistTheSelectionToLocalStorageInATryCatch()
    {
        string html = Html();

        Assert.Contains("ArrowRight", html);
        Assert.Contains("ArrowLeft", html);
        Assert.Contains("localStorage.setItem(TAB_KEY", html);
        // The set/get calls around TAB_KEY are wrapped, the same guard every other
        // localStorage/sessionStorage touch on this page already uses.
        int setItem = html.IndexOf("localStorage.setItem(TAB_KEY", StringComparison.Ordinal);
        string aroundSetItem = html[Math.Max(0, setItem - 40)..(setItem + 80)];
        Assert.Contains("try", aroundSetItem);
        Assert.Contains("catch", aroundSetItem);
    }

    // -- fizzle rate by spell line removed ------------------------------------------------------

    [Fact]
    public void TheFizzleRateBySpellLineChartAndItsNoteAreGone()
    {
        string html = Html();

        Assert.DoesNotContain("Fizzle rate by spell line", html);
        Assert.DoesNotContain("id=\"fizzles\"", html);
        Assert.DoesNotContain("id=\"fizzles-empty\"", html);
        Assert.DoesNotContain("drawFizzles", html);
        Assert.DoesNotContain("fizzlesSignature", html);
        Assert.DoesNotContain("Rate is confirmed fizzles over attempts", html);
    }

    [Fact]
    public void TheOverallFizzleRateTileSurvivesTheRemoval()
    {
        // Not "the same feature" as the by-line breakdown: one headline percentage across every
        // line, not a per-line chart, so it stays in place.
        string html = Html();

        Assert.Contains("id=\"tile-fizzle-value\"", html);
        Assert.Contains("<h3>Fizzle rate</h3>", html);
    }

    [Fact]
    public void TheFizzleTotalsCounterSurvivesTheRemoval()
    {
        string html = Html();

        Assert.Contains("id=\"count-fizzles\"", html);
    }

    // -- casts-per-hour chart carries a fizzle series -------------------------------------------

    [Fact]
    public void TheHoursChartAriaLabelAndLegendMentionFizzles()
    {
        string html = Html();

        int hoursChart = html.IndexOf("id=\"hours\"", StringComparison.Ordinal);
        int hoursChartEnd = html.IndexOf("</svg>", hoursChart, StringComparison.Ordinal);
        string hoursChartTag = html[hoursChart..hoursChartEnd];
        Assert.Contains("fizzle", hoursChartTag, StringComparison.OrdinalIgnoreCase);

        int legendStart = html.IndexOf("<div class=\"legend\">", hoursChartEnd, StringComparison.Ordinal);
        int legendEnd = html.IndexOf("</div>", legendStart, StringComparison.Ordinal);
        string legend = html[legendStart..legendEnd];
        Assert.Contains("Fizzles", legend);
        Assert.Contains("var(--alert)", legend);
    }

    [Fact]
    public void DrawHoursReadsTheFizzlesFieldOffEachHourlyBucket()
    {
        string html = Html();

        Assert.Contains("h.fizzles", html);
    }

    // -- components: fixed slot order ------------------------------------------------------------

    [Fact]
    public void ComponentSlotOrderMatchesTheBriefsRowPairing()
    {
        string html = Html();

        int listStart = html.IndexOf("COMPONENT_SLOT_ORDER = [", StringComparison.Ordinal);
        Assert.True(listStart >= 0, "no COMPONENT_SLOT_ORDER table in the page");
        int listEnd = html.IndexOf("];", listStart, StringComparison.Ordinal);
        string list = html[listStart..listEnd];

        string[] expectedOrder =
        [
            "Prismatic Taper", "Mana Scarab",
            "Platinum Scarab", "Prismatic Pea",
            "Pyreal Scarab", "Pyreal Pea",
            "Gold Scarab", "Gold Pea",
            "Silver Scarab", "Silver Pea",
            "Copper Scarab", "Copper Pea",
            "Iron Scarab", "Iron Pea",
            "Lead Scarab", "Lead Pea",
        ];

        int cursor = 0;
        foreach (string name in expectedOrder)
        {
            int found = list.IndexOf("\"" + name + "\"", cursor, StringComparison.Ordinal);
            Assert.True(found >= 0, $"'{name}' missing, or out of order, in COMPONENT_SLOT_ORDER");
            cursor = found + name.Length;
        }
    }

    [Fact]
    public void EveryComponentSlotAlwaysRendersEvenAtZeroStock()
    {
        string html = Html();

        // A slot missing from the hub's own items array still gets a row, at zero stock and
        // marked not held, rather than being left off the grid.
        Assert.Contains("held: false", html);
        Assert.Contains("stock: 0, usedBy: 0", html);
    }

    [Fact]
    public void AReagentOutsideTheFixedTableIsAppendedAfterItRatherThanDropped()
    {
        string html = Html();

        Assert.Contains("COMPONENT_SLOT_ORDER.indexOf(c.name) === -1", html);
    }

    // -- copy: "coming soon" replaces "free only" ------------------------------------------------

    [Fact]
    public void AccessPolicyReadsComingSoonRatherThanFreeOnly()
    {
        string html = Html();

        Assert.Contains("<span class=\"phase2\">coming soon</span>", html);
        Assert.DoesNotContain("free only", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSampleDataNoteNoLongerClaimsTheBuildShipsFreeOnly()
    {
        string html = Html();

        Assert.DoesNotContain("the build ships free only", html, StringComparison.OrdinalIgnoreCase);
    }
}

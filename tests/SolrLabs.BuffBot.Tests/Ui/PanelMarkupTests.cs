using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SolrLabs.BuffBot.Ui;

namespace SolrLabs.BuffBot.Tests.Ui;

public sealed class PanelMarkupTests
{
    [Fact]
    public void MarkupIsWellFormedXml()
    {
        XDocument.Load(MarkupPath());
    }

    [Fact]
    public void EveryBindingNamesAPublicPropertyOnTheViewModel()
    {
        XDocument markup = XDocument.Load(MarkupPath());
        var missing = new List<string>();

        foreach (XAttribute attribute in markup.Descendants().Attributes())
        {
            foreach (Match binding in Regex.Matches(attribute.Value, @"^\{(\w+)\}$"))
            {
                string name = binding.Groups[1].Value;
                if (typeof(BuffBotPanelViewModel).GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is null)
                    missing.Add($"{attribute.Parent!.Name.LocalName}.{attribute.Name.LocalName}={{{name}}}");
            }
        }

        Assert.Empty(missing);
    }

    private static string MarkupPath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BuffBot.slnx")))
                return Path.Combine(directory.FullName, "src", "SolrLabs.BuffBot", "buffbot.xml");
        }

        throw new InvalidOperationException("BuffBot.slnx not found above " + AppContext.BaseDirectory);
    }
}

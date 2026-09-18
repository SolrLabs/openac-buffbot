using System.Diagnostics;

namespace SolrLabs.BuffBot.Ui;

/// <summary>The seam between the operator panel's Open web console button and the OS, so a test can drive <see cref="BuffBotPanelViewModel.OpenConsole"/> against a fake instead of starting a process.</summary>
internal interface IBrowserLauncher
{
    void Open(string url);
}

/// <summary>The only place in the plugin that starts an OS process.</summary>
internal sealed class ProcessBrowserLauncher : IBrowserLauncher
{
    public void Open(string url)
    {
        using Process? process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

/// <summary>Never launches anything — the headless host's launcher, and the panel's own default.</summary>
internal sealed class NullBrowserLauncher : IBrowserLauncher
{
    internal static readonly NullBrowserLauncher Instance = new();

    private NullBrowserLauncher()
    {
    }

    public void Open(string url)
    {
    }
}

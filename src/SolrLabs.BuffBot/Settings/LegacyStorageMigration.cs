using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Settings;

/// <summary>Copies a value across from <see cref="ILegacyStorageSource"/> the first time the host's own storage has nothing under that key, once per install. A marker key records completion, so a later run returns immediately; a key the host already holds a value for, even an empty one, is left exactly as it is.</summary>
internal static class LegacyStorageMigration
{
    internal const string MarkerKey = "migrated/legacy-v1";

    internal static void Run(
        IPluginStorage hostStorage, ILegacyStorageSource? legacySource, Action<string> logInfo, Action<string> logWarn)
    {
        if (!hostStorage.IsAvailable || hostStorage.ReadText(MarkerKey) is not null)
            return;

        // Unavailable, or the folder was never there in the first place: nothing to copy, and
        // no marker either, so a later start looks again instead of assuming this settled it.
        if (legacySource is not { IsAvailable: true } source)
            return;

        try
        {
            var countsByNamespace = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (string key in source.ListKeys())
            {
                if (hostStorage.ReadText(key) is not null)
                    continue;

                string? value = source.ReadText(key);
                if (value is null)
                    continue;

                hostStorage.WriteText(key, value);
                string ns = TopLevelNamespace(key);
                countsByNamespace[ns] = countsByNamespace.GetValueOrDefault(ns) + 1;
            }

            hostStorage.WriteText(MarkerKey, "1");

            if (countsByNamespace.Count > 0)
            {
                string detail = string.Join(", ", countsByNamespace.Select(pair => $"{pair.Value} {pair.Key}"));
                logInfo($"BuffBot brought {detail} key(s) over from the pre-single-root storage layout.");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logWarn($"BuffBot legacy storage migration skipped: {error.Message}");
        }
    }

    private static string TopLevelNamespace(string key)
    {
        int slash = key.IndexOf('/');
        return slash < 0 ? key : key[..slash];
    }
}

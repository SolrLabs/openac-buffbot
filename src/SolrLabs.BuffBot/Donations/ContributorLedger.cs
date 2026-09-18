using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Donations;

/// <summary><see cref="UtcTime"/> is the moment the trade that delivered it completed.</summary>
internal readonly record struct ContributorEntry(string ItemName, uint WeenieClassId, int Count, DateTime UtcTime);

/// <summary>One donor's full history against one bot character, grouped for the Contributors tab.</summary>
internal readonly record struct Contributor(uint DonorObjectId, string DonorName, IReadOnlyList<ContributorEntry> Entries);

/// <summary>A file that fails to parse is never touched again — <see cref="Append"/> starts a
/// sibling <c>.recovered-&lt;n&gt;</c> file instead, and <see cref="Load"/> merges whichever parse.</summary>
internal sealed class ContributorLedger
{
    private const int Version = 1;

    /// <summary>Stops a pathological storage from looping forever; real use never comes close.</summary>
    private const int MaxRecoveredFileAttempts = 1000;

    private readonly IPluginStorage _storage;
    private readonly Action<string> _warn;

    internal ContributorLedger(IPluginStorage storage, Action<string> warn)
    {
        _storage = storage;
        _warn = warn;
    }

    internal IReadOnlyList<Contributor> Load(uint characterObjectId)
    {
        if (!_storage.IsAvailable)
            return Array.Empty<Contributor>();

        var byDonor = new Dictionary<uint, (string Name, List<ContributorEntry> Entries)>();

        foreach (string key in FileKeys(characterObjectId))
        {
            string? json = _storage.ReadText(key);
            if (json is null)
                continue;

            if (!TryParse(json, out List<(uint DonorObjectId, string DonorName, List<ContributorEntry> Entries)>? donors))
            {
                _warn($"[donations] {key} is corrupt or unreadable; skipping it.");
                continue;
            }

            foreach ((uint donorObjectId, string donorName, List<ContributorEntry> entries) in donors!)
            {
                if (!byDonor.TryGetValue(donorObjectId, out (string Name, List<ContributorEntry> Entries) existing))
                {
                    existing = (donorName, new List<ContributorEntry>());
                    byDonor[donorObjectId] = existing;
                }
                else if (!string.IsNullOrEmpty(donorName))
                    byDonor[donorObjectId] = (donorName, existing.Entries);

                existing.Entries.AddRange(entries);
            }
        }

        var result = new List<Contributor>(byDonor.Count);
        foreach (KeyValuePair<uint, (string Name, List<ContributorEntry> Entries)> kv in byDonor)
            result.Add(new Contributor(kv.Key, kv.Value.Name, kv.Value.Entries));

        result.Sort((a, b) => MostRecent(b.Entries).CompareTo(MostRecent(a.Entries)));
        return result;
    }

    internal void Append(uint characterObjectId, CompletedDonation donation)
    {
        if (!_storage.IsAvailable || donation.Items.Count == 0)
            return;

        var newEntries = new List<ContributorEntry>(donation.Items.Count);
        foreach (DonatedItem item in donation.Items)
            newEntries.Add(new ContributorEntry(item.Name, item.WeenieClassId, item.Count, donation.UtcTime));

        if (TryAppendToFile(PrimaryKey(characterObjectId), donation.DonorObjectId, donation.DonorName, newEntries))
            return;

        string recoveredPrefix = RecoveredPrefix(characterObjectId);
        for (int n = 1; n <= MaxRecoveredFileAttempts; n++)
            if (TryAppendToFile($"{recoveredPrefix}{n}", donation.DonorObjectId, donation.DonorName, newEntries))
                return;
    }

    /// <summary>False only when the file exists and is corrupt, so the caller moves on to the next recovered file.</summary>
    private bool TryAppendToFile(string key, uint donorObjectId, string donorName, List<ContributorEntry> newEntries)
    {
        string? json = _storage.ReadText(key);
        List<(uint DonorObjectId, string DonorName, List<ContributorEntry> Entries)> donors;

        if (json is null)
            donors = new();
        else if (TryParse(json, out List<(uint DonorObjectId, string DonorName, List<ContributorEntry> Entries)>? parsed))
            donors = parsed!;
        else
        {
            _warn($"[donations] {key} is corrupt or unreadable; leaving it untouched and continuing elsewhere.");
            return false;
        }

        int index = donors.FindIndex(d => d.DonorObjectId == donorObjectId);
        if (index < 0)
            donors.Add((donorObjectId, donorName, newEntries));
        else
        {
            (uint id, string _, List<ContributorEntry> entries) = donors[index];
            entries.AddRange(newEntries);
            donors[index] = (id, donorName, entries);
        }

        _storage.WriteText(key, Serialize(donors));
        return true;
    }

    private static DateTime MostRecent(IReadOnlyList<ContributorEntry> entries)
    {
        DateTime latest = DateTime.MinValue;
        foreach (ContributorEntry entry in entries)
            if (entry.UtcTime > latest)
                latest = entry.UtcTime;
        return latest;
    }

    private IEnumerable<string> FileKeys(uint characterObjectId)
    {
        yield return PrimaryKey(characterObjectId);

        string recoveredPrefix = RecoveredPrefix(characterObjectId);
        foreach (string key in _storage.List("contributors/"))
            if (key.StartsWith(recoveredPrefix, StringComparison.Ordinal))
                yield return key;
    }

    private static string PrimaryKey(uint characterObjectId) => $"contributors/{characterObjectId}";

    private static string RecoveredPrefix(uint characterObjectId) => $"contributors/{characterObjectId}.recovered-";

    private static string Serialize(List<(uint DonorObjectId, string DonorName, List<ContributorEntry> Entries)> donors)
    {
        var donorsArray = new JsonArray();
        foreach ((uint donorObjectId, string donorName, List<ContributorEntry> entries) in donors)
        {
            var entriesArray = new JsonArray();
            foreach (ContributorEntry entry in entries)
            {
                entriesArray.Add(new JsonObject
                {
                    ["item"] = entry.ItemName,
                    ["wcid"] = entry.WeenieClassId,
                    ["count"] = entry.Count,
                    ["utc"] = entry.UtcTime.ToString("O"),
                });
            }

            donorsArray.Add(new JsonObject
            {
                ["donorObjectId"] = donorObjectId,
                ["donorName"] = donorName,
                ["entries"] = entriesArray,
            });
        }

        return new JsonObject { ["version"] = Version, ["donors"] = donorsArray }.ToJsonString();
    }

    private static bool TryParse(
        string json, out List<(uint DonorObjectId, string DonorName, List<ContributorEntry> Entries)>? donors)
    {
        donors = null;

        JsonObject? root = TryParseObject(json);
        if (root is null)
            return false;
        if (!root.TryGetPropertyValue("donors", out JsonNode? donorsNode) || donorsNode is not JsonArray donorsArray)
            return false;

        var result = new List<(uint, string, List<ContributorEntry>)>(donorsArray.Count);
        foreach (JsonNode? donorNode in donorsArray)
        {
            if (donorNode is not JsonObject donorObject)
                return false;
            if (!TryUInt(donorObject, "donorObjectId", out uint donorObjectId))
                return false;
            if (!donorObject.TryGetPropertyValue("entries", out JsonNode? entriesNode) || entriesNode is not JsonArray entriesArray)
                return false;

            string donorName = TryString(donorObject, "donorName") ?? string.Empty;

            var entries = new List<ContributorEntry>(entriesArray.Count);
            foreach (JsonNode? entryNode in entriesArray)
            {
                if (entryNode is not JsonObject entryObject)
                    return false;
                if (!TryUInt(entryObject, "wcid", out uint wcid))
                    return false;
                if (!TryInt(entryObject, "count", out int count))
                    return false;
                if (!TryDateTime(entryObject, "utc", out DateTime utc))
                    return false;

                string itemName = TryString(entryObject, "item") ?? string.Empty;
                entries.Add(new ContributorEntry(itemName, wcid, count, utc));
            }

            result.Add((donorObjectId, donorName, entries));
        }

        donors = result;
        return true;
    }

    private static JsonObject? TryParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryString(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static bool TryUInt(JsonObject root, string name, out uint value)
    {
        value = 0;
        return root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out value);
    }

    private static bool TryInt(JsonObject root, string name, out int value)
    {
        value = 0;
        return root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out value);
    }

    private static bool TryDateTime(JsonObject root, string name, out DateTime value)
    {
        value = default;
        if (!root.TryGetPropertyValue(name, out JsonNode? node) || node is not JsonValue v || !v.TryGetValue(out string? s))
            return false;

        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }
}

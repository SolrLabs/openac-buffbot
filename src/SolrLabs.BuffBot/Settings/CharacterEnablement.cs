using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Settings;

/// <summary>The plugin is inert until turned on for the current character; the choice survives relogin, keyed by the character's object id, which is stable across a display-name change.</summary>
internal sealed class CharacterEnablement
{
    private const string EnabledValue = "true";
    private const string DisabledValue = "false";

    private readonly IPluginStorage _storage;

    internal CharacterEnablement(IPluginStorage storage) => _storage = storage;

    internal bool IsEnabled(uint characterObjectId) =>
        _storage.IsAvailable && _storage.ReadText(Key(characterObjectId)) == EnabledValue;

    internal void SetEnabled(uint characterObjectId, bool enabled)
    {
        if (!_storage.IsAvailable)
            return;

        _storage.WriteText(Key(characterObjectId), enabled ? EnabledValue : DisabledValue);
    }

    private static string Key(uint characterObjectId) => $"enabled/{characterObjectId}";
}

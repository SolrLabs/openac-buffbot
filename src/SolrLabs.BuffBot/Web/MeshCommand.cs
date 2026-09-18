namespace SolrLabs.BuffBot.Web;

/// <summary>The operator writes the console page may make. Every command crosses into the tick only through <see cref="MeshNode.TryDequeueCommand"/>'s queue; nothing on the network side ever calls <see cref="AcDream.Plugin.Abstractions.IPluginHost"/> or <see cref="Guard.LoopGuard"/> directly.</summary>
internal enum MeshCommandKind
{
    Mute,
    Release,
    Enable,
    Disable,
    Settings,
    Drain,
}

/// <summary><see cref="ObjectId"/> is required for <see cref="MeshCommandKind.Mute"/> and <see cref="MeshCommandKind.Release"/>, absent for every other kind. <see cref="Settings"/> is present only for <see cref="MeshCommandKind.Settings"/>.</summary>
internal sealed record MeshCommand(MeshCommandKind Kind, uint? ObjectId, MeshSettingsPatch? Settings = null);

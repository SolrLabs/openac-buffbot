namespace SolrLabs.BuffBot.Web;

/// <summary>Where this node currently sits on the mesh. <see cref="Starting"/> covers a hub still inside its startup grace window: the key it already holds might still rotate, so nothing offers it as a link yet.</summary>
internal enum MeshConsoleState
{
    Unavailable,
    Starting,
    Hub,
    Spoke,
}

/// <summary>A throw-free snapshot of <see cref="MeshConsoleState"/> plus the link an operator could open right now, if the key has settled on one. <see cref="Link"/> is never set for <see cref="MeshConsoleState.Starting"/>, <see cref="MeshConsoleState.Unavailable"/>, or a <see cref="MeshConsoleState.Spoke"/> whose key copy is not yet <see cref="MeshKeyStore.IsValidKey"/>.</summary>
internal readonly record struct MeshConsoleLink(MeshConsoleState State, string? Link);

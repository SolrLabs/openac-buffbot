namespace SolrLabs.BuffBot.Web;

/// <summary>Where this node currently sits on the mesh.</summary>
internal enum MeshConsoleState
{
    Unavailable,
    Hub,
    Spoke,
}

/// <summary>A throw-free snapshot of <see cref="MeshConsoleState"/> plus the link an operator could open right now.</summary>
internal readonly record struct MeshConsoleLink(MeshConsoleState State, string? Link);

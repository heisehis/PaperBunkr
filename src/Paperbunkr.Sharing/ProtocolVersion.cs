namespace Paperbunkr.Sharing;

/// <summary>Wire protocol version. Both sides refuse to talk across a mismatch rather than guess.</summary>
public static class ProtocolVersion
{
    public const int Current = 1;

    public const string HeaderName = "X-Paperbunkr-Protocol";
}

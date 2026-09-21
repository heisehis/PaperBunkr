namespace Paperbunkr.Sharing.Tests;

public class ProtocolVersionTests
{
    [Fact]
    public void Current_IsOne_UntilTheWireFormatBreaks()
    {
        Assert.Equal(1, ProtocolVersion.Current);
        Assert.Equal("X-Paperbunkr-Protocol", ProtocolVersion.HeaderName);
    }
}

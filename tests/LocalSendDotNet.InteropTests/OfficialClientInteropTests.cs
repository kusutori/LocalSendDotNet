namespace LocalSendDotNet.InteropTests;

public sealed class OfficialClientInteropTests
{
    [Fact]
    public void OfficialClientCompatibilityMatrixSeparatesEvidenceFromManualChecks()
    {
        var matrix = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "interop-matrix.md"));
        Assert.Contains("## Observed on a real official client", matrix, StringComparison.Ordinal);
        Assert.Contains("## Automated protocol coverage", matrix, StringComparison.Ordinal);
        Assert.Contains("## Remaining manual official-client checks", matrix, StringComparison.Ordinal);
        Assert.Contains("Android LocalSend", matrix, StringComparison.Ordinal);
    }
}

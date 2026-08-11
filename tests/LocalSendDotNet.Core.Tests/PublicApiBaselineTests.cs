using LocalSendDotNet.ApiSurface;
using System.Security.Cryptography;
using System.Text;

namespace LocalSendDotNet.Core.Tests;

public sealed class PublicApiBaselineTests
{
    [Fact]
    public void ExportedApiMatchesApprovedBaseline()
    {
        var actual = PublicApiSurface.Create(typeof(LocalSendNode).Assembly);
        var baseline = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PublicApiBaseline.txt")).ReplaceLineEndings("\n").Trim();
        var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
        Assert.Equal(baseline, actualHash);
    }
}

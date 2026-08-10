using System.Security.Cryptography.X509Certificates;

namespace LocalSendDotNet.Core.Tests;

public sealed class IdentityTests
{
    [Fact]
    public async Task IdentityIsStableAcrossReloads()
    {
        var directory = TestDirectory.Create();
        try
        {
            string firstFingerprint;
            await using (var first = new AsyncDisposableIdentity(await DeviceIdentityStore.LoadOrCreateAsync(directory, default)))
                firstFingerprint = first.Identity.Fingerprint;
            await using var second = new AsyncDisposableIdentity(await DeviceIdentityStore.LoadOrCreateAsync(directory, default));
            Assert.Equal(firstFingerprint, second.Identity.Fingerprint);
            Assert.Equal(64, second.Identity.Fingerprint.Length);
            Assert.True(second.Identity.Certificate.HasPrivateKey);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task PeerFingerprintMustMatch()
    {
        var directory = TestDirectory.Create();
        try
        {
            await using var wrapper = new AsyncDisposableIdentity(await DeviceIdentityStore.LoadOrCreateAsync(directory, default));
            Assert.True(DeviceIdentityStore.ValidatePeerCertificate(wrapper.Identity.Certificate, wrapper.Identity.Fingerprint));
            Assert.False(DeviceIdentityStore.ValidatePeerCertificate(wrapper.Identity.Certificate, new string('0', 64)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class AsyncDisposableIdentity(DeviceIdentity identity) : IAsyncDisposable
    {
        public DeviceIdentity Identity => identity;
        public ValueTask DisposeAsync() { identity.Dispose(); return ValueTask.CompletedTask; }
    }
}

internal static class TestDirectory
{
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "LocalSendDotNet.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

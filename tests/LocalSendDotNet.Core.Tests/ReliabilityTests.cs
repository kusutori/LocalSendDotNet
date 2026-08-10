namespace LocalSendDotNet.Core.Tests;

public sealed class ReliabilityTests
{
    [Fact]
    public async Task ReliableBroadcastWaitsInsteadOfDroppingDecisionEvents()
    {
        var hub = new BroadcastHub<int>(1, dropOldest: false);
        await using var reader = hub.Subscribe(default).GetAsyncEnumerator();
        var firstRead = reader.MoveNextAsync().AsTask();
        hub.Publish(1);
        Assert.True(await firstRead);
        Assert.Equal(1, reader.Current);

        await hub.PublishAsync(2, default);
        var blocked = hub.PublishAsync(3, default);
        await Task.Delay(50);
        Assert.False(blocked.IsCompleted);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(2, reader.Current);
        await blocked;
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(3, reader.Current);
    }

    [Fact]
    public void DeviceStoreExpiresAndPublishesRemovedDevice()
    {
        var hub = new BroadcastHub<DeviceChange>(8);
        var store = new DeviceStore(hub);
        store.Upsert(new("old", "2.2", null, LocalSendDeviceType.Desktop, "fingerprint", false, [], DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.Equal(1, store.RemoveExpired(DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void ManuallyTrustedDeviceDoesNotExpire()
    {
        var store = new DeviceStore(new BroadcastHub<DeviceChange>(8));
        store.Upsert(new("manual", "2.2", null, LocalSendDeviceType.Desktop, "fingerprint", false, [],
            DateTimeOffset.UtcNow.AddDays(-1)), persistent: true);
        Assert.Equal(0, store.RemoveExpired(DateTimeOffset.UtcNow));
        Assert.Single(store.Snapshot());
    }

    [Fact]
    public void DirectoryItemsPreserveRelativePathsAndInferContentTypes()
    {
        var root = TestDirectory.Create();
        try
        {
            var child = Path.Combine(root, "nested");
            Directory.CreateDirectory(child);
            File.WriteAllText(Path.Combine(child, "note.txt"), "hello");
            var item = Assert.Single(LocalSendItems.FromDirectory(root));
            Assert.Equal("nested/note.txt", item.FileName);
            Assert.Equal("text/plain", item.ContentType);
            Assert.Equal("image/png", LocalSendContentTypes.GetForFileName("photo.PNG"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OptionsRejectUnsafeLimits()
    {
        var root = TestDirectory.Create();
        try
        {
            var options = new LocalSendOptions
            {
                Alias = "test",
                DataDirectory = root,
                DownloadDirectory = root,
                MaxIncomingItemsPerTransfer = 0
            };
            Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void EndpointNormalizesIpv4MappedAddress()
    {
        var endpoint = new DeviceEndpoint(System.Net.IPAddress.Parse("::ffff:192.168.1.10"), 53317, LocalSendProtocol.Https);
        Assert.Equal(System.Net.IPAddress.Parse("192.168.1.10"), endpoint.Address);
    }

    [Fact]
    public async Task StreamItemSupportsUiOwnedStreams()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("virtual file");
        var item = new SendStreamItem("virtual.txt", bytes.Length,
            _ => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
        Assert.Equal("text/plain", item.ContentType);
        await using var stream = await item.OpenReadAsync(default);
        using var reader = new StreamReader(stream);
        Assert.Equal("virtual file", await reader.ReadToEndAsync());
    }

    [Fact]
    public void PublicSurfaceKeepsProtocolDtosInternalAndUiEntryPointsStable()
    {
        var assembly = typeof(LocalSendNode).Assembly;
        Assert.DoesNotContain(assembly.GetExportedTypes(), type => type.Namespace?.Contains(".Protocol", StringComparison.Ordinal) == true);
        var methods = typeof(LocalSendNode).GetMethods().Select(static method => method.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var method in new[]
        {
            "StartAsync", "StopAsync", "RefreshAsync", "GetDevices", "WatchDeviceChangesAsync", "WatchIncomingTransfersAsync",
            "SendAsync", "AcceptAsync", "DeclineAsync", "CancelTransferAsync", "ProbeDeviceAsync", "AddKnownDeviceAsync", "RemoveDevice"
        })
            Assert.Contains(method, methods);
    }
}

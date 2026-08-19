using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LocalSendDotNet.Protocol.V2;
using Microsoft.Extensions.Logging.Abstractions;

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
            "SendAsync", "AcceptAsync", "DeclineAsync", "CancelTransferAsync", "ProbeDeviceAsync", "AddKnownDeviceAsync", "RemoveDevice",
            "StartWebShareAsync", "StopWebShare", "WatchWebShareAsync"
        })
            Assert.Contains(method, methods);
    }

    [Fact(Timeout = 15_000)]
    public async Task DiscoveryReceiverCanRebindAfterInterfaceRefresh()
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(static nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(static nic => nic.GetIPProperties().UnicastAddresses)
            .Select(static item => item.Address)
            .Where(static address => address.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToArray();
        if (addresses.Length == 0)
            return;
        var root = TestDirectory.Create();
        try
        {
            IReadOnlyList<IPAddress> supplied = [addresses[0]];
            var options = TestOptions(GetFreeUdpPort(), root);
            await using var discovery = new V2MulticastDiscovery(options, () => Announcement(options), (_, _, _) => Task.CompletedTask,
                NullLogger.Instance, () => supplied);
            await discovery.StartAsync(default);
            Assert.Equal(supplied, discovery.JoinedAddresses);
            supplied = [];
            Assert.False(await discovery.RefreshInterfacesAsync(force: true));
            Assert.NotEmpty(discovery.JoinedAddresses);
            supplied = addresses;
            Assert.True(await discovery.RefreshInterfacesAsync(force: true));
            Assert.Equal(addresses.OrderBy(static address => address.ToString(), StringComparer.Ordinal), discovery.JoinedAddresses);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact(Timeout = 15_000)]
    public async Task OccupiedUdpPortLeavesHttpServerRunning()
    {
        var root = TestDirectory.Create();
        using var blocker = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        blocker.ExclusiveAddressUse = true;
        blocker.Bind(new IPEndPoint(IPAddress.Any, 0));
        var port = ((IPEndPoint)blocker.LocalEndPoint!).Port;
        await using var node = new LocalSendNode(TestOptions(port, root));
        try
        {
            await node.StartAsync();
            Assert.Equal(LocalSendNodeState.Running, node.State);
            Assert.NotNull(node.Identity);
            Assert.NotNull(node.DiscoveryError);
            await node.StopAsync();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact(Timeout = 15_000)]
    public async Task OccupiedTcpPortProducesActionableException()
    {
        var root = TestDirectory.Create();
        var listener = new TcpListener(IPAddress.IPv6Any, 0);
        listener.Server.DualMode = true;
        listener.Server.ExclusiveAddressUse = true;
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var node = new LocalSendNode(TestOptions(port, root));
        try
        {
            var exception = await Assert.ThrowsAsync<PortUnavailableException>(() => node.StartAsync());
            Assert.Equal(port, exception.Port);
            Assert.Equal(LocalSendNodeState.Faulted, node.State);
            listener.Stop();
            await node.StartAsync();
            Assert.Equal(LocalSendNodeState.Running, node.State);
            await node.StopAsync();
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(Timeout = 20_000)]
    public async Task CancelledStartupCleansUpAndCanBeRetried()
    {
        var root = TestDirectory.Create();
        await using var node = new LocalSendNode(TestOptions(GetFreeTcpPort(), root));
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => node.StartAsync(cancellation.Token));
            Assert.Equal(LocalSendNodeState.Created, node.State);
            await node.StartAsync();
            Assert.Equal(LocalSendNodeState.Running, node.State);
            await node.StopAsync();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static int GetFreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static LocalSendOptions TestOptions(int port, string? root = null)
    {
        root ??= TestDirectory.Create();
        return new LocalSendOptions { Alias = "test", DataDirectory = Path.Combine(root, "data"), DownloadDirectory = Path.Combine(root, "downloads"), Port = port };
    }

    private static DeviceInfoDto Announcement(LocalSendOptions options) => new()
    {
        Alias = options.Alias,
        Version = "2.2",
        Fingerprint = new string('A', 64),
        Port = options.Port,
        Protocol = "https"
    };
}

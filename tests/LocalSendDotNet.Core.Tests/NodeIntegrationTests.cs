using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LocalSendDotNet.Core.Tests;

public sealed class NodeIntegrationTests
{
    [Fact(Timeout = 30_000)]
    public async Task TwoNodesTransferTextOverMutualTls()
    {
        var root = TestDirectory.Create();
        var senderData = Path.Combine(root, "sender");
        var receiverData = Path.Combine(root, "receiver");
        var downloads = Path.Combine(root, "downloads");
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        await using var sender = CreateNode("Sender", senderData, Path.Combine(root, "sender-downloads"), senderPort);
        await using var receiver = CreateNode("Receiver", receiverData, downloads, receiverPort);
        try
        {
            await Task.WhenAll(receiver.StartAsync(), sender.StartAsync());
            var receiveTask = AcceptNextAsync(receiver);
            await Task.Delay(100);

            var fingerprint = await ReadFingerprintAsync(receiverData);
            var device = new LocalSendDevice("Receiver", "2.2", null, LocalSendDeviceType.Desktop, fingerprint, false,
                [new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https)], DateTimeOffset.UtcNow);
            var sent = await sender.SendAsync(device, [new SendTextItem("hello from dotnet")]);
            var received = await receiveTask;

            Assert.Equal(TransferState.Completed, sent.State);
            Assert.Equal(TransferState.Completed, received.State);
            var saved = Assert.Single(received.Items).SavedPath;
            Assert.Equal("hello from dotnet", await File.ReadAllTextAsync(saved!));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact(Timeout = 30_000)]
    public async Task PinAndPartialAcceptanceAreEnforced()
    {
        var root = TestDirectory.Create();
        var senderData = Path.Combine(root, "sender");
        var receiverData = Path.Combine(root, "receiver");
        var downloads = Path.Combine(root, "downloads");
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        await using var sender = CreateNode("Sender", senderData, Path.Combine(root, "sender-downloads"), senderPort);
        await using var receiver = CreateNode("Receiver", receiverData, downloads, receiverPort, "2468");
        try
        {
            await Task.WhenAll(receiver.StartAsync(), sender.StartAsync());
            var fingerprint = await ReadFingerprintAsync(receiverData);
            var device = new LocalSendDevice("Receiver", "2.2", null, LocalSendDeviceType.Desktop, fingerprint, false,
                [new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https)], DateTimeOffset.UtcNow);
            var items = new SendItem[] { new SendTextItem("one", "one.txt"), new SendTextItem("two", "two.txt") };

            await Assert.ThrowsAsync<PinRequiredException>(() => sender.SendAsync(device, items));
            var wrong = await Assert.ThrowsAsync<PinRequiredException>(() => sender.SendAsync(device, items, new SendOptions { Pin = "0000" }));
            Assert.True(wrong.InvalidPin);

            var receiveTask = AcceptNextAsync(receiver, request => [request.Items.Single(static x => x.FileName == "one.txt").Id]);
            await Task.Delay(100);
            var sent = await sender.SendAsync(device, items, new SendOptions { Pin = "2468" });
            var received = await receiveTask;

            Assert.Equal(TransferState.Completed, sent.State);
            Assert.Single(sent.Items);
            Assert.Equal("one.txt", Assert.Single(received.Items).FileName);
            Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(downloads, "one.txt")));
            Assert.False(File.Exists(Path.Combine(downloads, "two.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static LocalSendNode CreateNode(string alias, string data, string downloads, int port, string? receivePin = null) => new(new LocalSendOptions
    {
        Alias = alias,
        DataDirectory = data,
        DownloadDirectory = downloads,
        Port = port,
        RequestTimeout = TimeSpan.FromSeconds(5),
        IncomingDecisionTimeout = TimeSpan.FromSeconds(5),
        ReceivePin = receivePin
    });

    private static async Task<TransferResult> AcceptNextAsync(LocalSendNode node)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync())
            return await node.AcceptAsync(request.RequestId);
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private static async Task<TransferResult> AcceptNextAsync(LocalSendNode node, Func<IncomingTransferRequest, IReadOnlyCollection<string>> select)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync())
            return await node.AcceptAsync(request.RequestId, new AcceptTransferOptions { AcceptedItemIds = select(request) });
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private static async Task<string> ReadFingerprintAsync(string dataDirectory)
    {
        using var identity = await DeviceIdentityStore.LoadOrCreateAsync(dataDirectory, default);
        return identity.Fingerprint;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

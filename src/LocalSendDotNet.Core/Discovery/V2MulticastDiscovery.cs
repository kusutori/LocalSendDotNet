using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using LocalSendDotNet.Protocol.V2;
using Microsoft.Extensions.Logging;

namespace LocalSendDotNet;

internal sealed class V2MulticastDiscovery(
    LocalSendOptions options,
    Func<DeviceInfoDto> createAnnouncement,
    Func<DeviceInfoDto, IPAddress, CancellationToken, Task> onAnnouncement,
    ILogger logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Socket? _receiver;
    private Task? _receiveLoop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        receiver.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        receiver.ExclusiveAddressUse = false;
        receiver.Bind(new IPEndPoint(IPAddress.Any, options.Port));
        var joined = 0;
        foreach (var address in GetUsableAddresses())
        {
            try
            {
                receiver.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(options.MulticastAddress, address));
                joined++;
            }
            catch (SocketException exception)
            {
                logger.LogDebug(exception, "Could not join LocalSend multicast on {Address}", address);
            }
        }
        if (joined == 0)
        {
            receiver.Dispose();
            throw new LocalSendException("No usable IPv4 interface could join the LocalSend multicast group.");
        }

        _receiver = receiver;
        _receiveLoop = ReceiveLoopAsync(receiver, _stop.Token);
        return Task.CompletedTask;
    }

    public async Task AnnounceAsync(CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(createAnnouncement(), V2Json.Options);
        var delays = new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2) };
        foreach (var delay in delays)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await SendOnAllInterfacesAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(Socket receiver, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await receiver.ReceiveFromAsync(buffer, SocketFlags.None, remote, cancellationToken).ConfigureAwait(false);
                var message = JsonSerializer.Deserialize<DeviceInfoDto>(buffer.AsSpan(0, result.ReceivedBytes), V2Json.Options);
                if (message is null || result.RemoteEndPoint is not IPEndPoint endpoint)
                    continue;
                _ = ObserveAnnouncementAsync(message, endpoint.Address, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "LocalSend multicast receive failed");
            }
        }
    }

    private async Task ObserveAnnouncementAsync(DeviceInfoDto message, IPAddress source, CancellationToken cancellationToken)
    {
        try { await onAnnouncement(message, source, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogDebug(exception, "Could not register with announced LocalSend peer {Address}", source); }
    }

    private async Task SendOnAllInterfacesAsync(byte[] payload, CancellationToken cancellationToken)
    {
        foreach (var address in GetUsableAddresses())
        {
            try
            {
                using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                sender.Bind(new IPEndPoint(address, 0));
                await sender.SendToAsync(payload, SocketFlags.None, new IPEndPoint(options.MulticastAddress, options.Port), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or NetworkInformationException)
            {
                logger.LogDebug(exception, "Could not announce LocalSend on {Address}", address);
            }
        }
    }

    private static IEnumerable<IPAddress> GetUsableAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(static nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
        .SelectMany(static nic => nic.GetIPProperties().UnicastAddresses)
        .Select(static address => address.Address)
        .Where(static address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
        .Distinct();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _receiver?.Dispose();
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
        }
        _stop.Dispose();
    }
}

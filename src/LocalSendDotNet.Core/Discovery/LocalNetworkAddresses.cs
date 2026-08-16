using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LocalSendDotNet;

internal static class LocalNetworkAddresses
{
    internal static IReadOnlyList<IPAddress> GetUnicastIPv4() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(static nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
        .SelectMany(static nic => nic.GetIPProperties().UnicastAddresses)
        .Select(static address => address.Address)
        .Where(static address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
        .Distinct()
        .ToArray();

    internal static bool IsAutomaticPrivate(IPAddress address)
    {
        var octets = address.GetAddressBytes();
        return octets.Length == 4 && octets[0] == 169 && octets[1] == 254;
    }
}

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LocalSendDotNet;

internal static class LocalNetworkAddresses
{
    internal static IReadOnlyList<IPAddress> GetUnicastIPv4() =>
        GetUnicastIPv4(whitelist: null, blacklist: null);

    internal static IReadOnlyList<IPAddress> GetUnicastIPv4(
        IReadOnlyList<string>? whitelist,
        IReadOnlyList<string>? blacklist)
    {
        var addresses = new List<IPAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var interfaceAddresses = nic.GetIPProperties().UnicastAddresses
                .Select(static item => item.Address)
                .Where(static address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Distinct()
                .ToArray();
            if (interfaceAddresses.Length == 0)
                continue;
            if (NetworkAddressPatterns.IsInterfaceIgnored(
                interfaceAddresses.Select(static address => address.ToString()),
                whitelist,
                blacklist))
                continue;

            addresses.AddRange(interfaceAddresses);
        }

        return addresses.Distinct().ToArray();
    }

    internal static bool IsAutomaticPrivate(IPAddress address)
    {
        var octets = address.GetAddressBytes();
        return octets.Length == 4 && octets[0] == 169 && octets[1] == 254;
    }
}

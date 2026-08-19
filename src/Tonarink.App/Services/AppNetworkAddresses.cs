using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LocalSendDotNet;

static class AppNetworkAddresses
{
    public static IReadOnlyList<string> ListIpv4(AppSettings settings)
    {
        var addresses = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var interfaceAddresses = nic.GetIPProperties().UnicastAddresses
                .Select(static item => item.Address)
                .Where(static address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(static address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (interfaceAddresses.Length == 0)
                continue;
            if (NetworkAddressPatterns.IsInterfaceIgnored(
                interfaceAddresses,
                settings.NetworkWhitelist,
                settings.NetworkBlacklist))
                continue;

            addresses.AddRange(interfaceAddresses);
        }

        return addresses.Distinct(StringComparer.Ordinal).ToArray();
    }
}

namespace LocalSendDotNet;

/// <summary>Matches IPv4 addresses against LocalSend-style whitelist and blacklist patterns.</summary>
public static class NetworkAddressPatterns
{
    /// <summary>
    /// Returns whether a network interface should be excluded from discovery.
    /// </summary>
    /// <param name="addresses">IPv4 addresses assigned to the interface.</param>
    /// <param name="whitelist">When not <see langword="null"/>, the interface is used only if at least one address matches a pattern.</param>
    /// <param name="blacklist">When not <see langword="null"/> and no whitelist is set, the interface is excluded if any address matches a pattern.</param>
    /// <returns><see langword="true"/> when the interface should be ignored.</returns>
    /// <remarks>
    /// A <c>null</c> whitelist and blacklist use every interface. Empty patterns never match.
    /// A <c>*</c> in a dotted quad matches one decimal octet, for example <c>192.168.1.*</c>.
    /// When both lists are set, the whitelist takes precedence.
    /// </remarks>
    public static bool IsInterfaceIgnored(
        IEnumerable<string> addresses,
        IReadOnlyList<string>? whitelist,
        IReadOnlyList<string>? blacklist)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var list = addresses as IReadOnlyList<string> ?? addresses.ToArray();
        if (whitelist is not null)
            return !list.Any(address => MatchesAny(whitelist, address));
        if (blacklist is not null)
            return list.Any(address => MatchesAny(blacklist, address));
        return false;
    }

    /// <summary>Returns whether an IPv4 address matches a dotted-quad pattern that may contain <c>*</c> octets.</summary>
    /// <param name="pattern">A dotted quad such as <c>192.168.1.20</c> or <c>192.168.1.*</c>.</param>
    /// <param name="address">The IPv4 address to test.</param>
    public static bool Matches(string pattern, string address)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(address))
            return false;

        var patternParts = pattern.Trim().Split('.');
        var addressParts = address.Trim().Split('.');
        if (patternParts.Length != 4 || addressParts.Length != 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            if (patternParts[i] == "*")
                continue;
            if (!string.Equals(patternParts[i], addressParts[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool MatchesAny(IReadOnlyList<string> patterns, string address)
    {
        for (var i = 0; i < patterns.Count; i++)
        {
            if (Matches(patterns[i], address))
                return true;
        }

        return false;
    }
}

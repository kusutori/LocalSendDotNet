namespace LocalSendDotNet;

internal static class SafeFileTarget
{
    public static string ResolveUnique(string root, string requestedName, ISet<string>? reserved = null)
    {
        var rootFull = Path.GetFullPath(root);
        Directory.CreateDirectory(rootFull);
        if (Path.IsPathRooted(requestedName) || requestedName.Contains(':', StringComparison.Ordinal))
            throw new LocalSendException($"Unsafe incoming path: {requestedName}");

        var components = requestedName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0 || components.Any(static x => x is "." or ".."))
            throw new LocalSendException($"Unsafe incoming path: {requestedName}");

        var sanitized = components.Select(SanitizeComponent).ToArray();
        var candidate = Path.GetFullPath(Path.Combine([rootFull, .. sanitized]));
        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new LocalSendException($"Incoming path escapes the destination: {requestedName}");

        Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
        if (!File.Exists(candidate) && (reserved is null || reserved.Add(candidate)))
            return candidate;

        var directory = Path.GetDirectoryName(candidate)!;
        var stem = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);
        for (var index = 1; ; index++)
        {
            var alternate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(alternate) && (reserved is null || reserved.Add(alternate)))
                return alternate;
        }
    }

    private static string SanitizeComponent(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray();
        var sanitized = new string(chars).Trim();
        if (sanitized.Length == 0 || sanitized is "." or "..")
            throw new LocalSendException($"Unsafe incoming filename component: {value}");
        return sanitized;
    }
}

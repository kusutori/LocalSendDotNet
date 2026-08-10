namespace LocalSendDotNet;

/// <summary>Helpers for creating send items without UI-specific dependencies.</summary>
public static class LocalSendItems
{
    /// <summary>Creates one send item for every file below <paramref name="directory"/>.</summary>
    public static IReadOnlyList<SendFileItem> FromDirectory(string directory, bool recursive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(root, "*", search)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SendFileItem(path, Path.GetRelativePath(root, path).Replace('\\', '/')))
            .ToArray();
    }
}

/// <summary>Small dependency-free MIME type map for common transfer formats.</summary>
public static class LocalSendContentTypes
{
    private static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".json"] = "application/json",
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".mp4"] = "video/mp4",
        [".mkv"] = "video/x-matroska",
        [".zip"] = "application/zip",
        [".7z"] = "application/x-7z-compressed",
        [".gz"] = "application/gzip"
    };

    public static string GetForFileName(string fileName) => Known.GetValueOrDefault(Path.GetExtension(fileName), "application/octet-stream");
}

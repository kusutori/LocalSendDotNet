using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Tonarink.WidgetProvider;

internal sealed record WidgetSnapshot(
    string Title,
    string Alias,
    string Address,
    string Status,
    string OpenLabel)
{
    private const string SettingsFileName = "settings.json";
    private const int DefaultPort = 53317;

    public static WidgetSnapshot Capture()
    {
        var settings = ReadSettings();
        var chinese = IsChinese(settings.Language);
        var alias = string.IsNullOrWhiteSpace(settings.Alias)
            ? (string.IsNullOrWhiteSpace(Environment.UserName) ? Environment.MachineName : Environment.UserName)
            : settings.Alias.Trim();
        var port = settings.Port is >= 1 and <= ushort.MaxValue ? settings.Port.Value : DefaultPort;
        var addresses = ListIpv4();
        var address = addresses.Count == 0
            ? (chinese ? $"端口 {port}" : $"Port {port}")
            : string.Join(chinese ? "，" : ", ", addresses.Select(item => $"{item}:{port}"));

        return new(
            Title: "Tonarink",
            Alias: alias,
            Address: address,
            Status: chinese ? "可以接收附近设备发来的文件。" : "Ready to receive files from nearby devices.",
            OpenLabel: chinese ? "打开 Tonarink" : "Open Tonarink");
    }

    public string ToJson() =>
        $$"""{"title":{{Quote(Title)}},"alias":{{Quote(Alias)}},"address":{{Quote(Address)}},"status":{{Quote(Status)}},"openLabel":{{Quote(OpenLabel)}}}""";

    private static string Quote(string value) => $"\"{JsonEncodedText.Encode(value)}\"";

    private static (string? Alias, int? Port, string? Language) ReadSettings()
    {
        try
        {
            foreach (var directory in SettingsDirectories())
            {
                var path = Path.Combine(directory, SettingsFileName);
                if (!File.Exists(path))
                    continue;

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                var alias = root.TryGetProperty("Alias", out var aliasValue) && aliasValue.ValueKind == JsonValueKind.String
                    ? aliasValue.GetString()
                    : null;
                var language = root.TryGetProperty("Language", out var languageValue) && languageValue.ValueKind == JsonValueKind.String
                    ? languageValue.GetString()
                    : null;
                int? port = root.TryGetProperty("Port", out var portValue) && portValue.TryGetInt32(out var parsed)
                    ? parsed
                    : null;
                return (alias, port, language);
            }
        }
        catch
        {
        }

        return (null, null, null);
    }

    private static IEnumerable<string> SettingsDirectories()
    {
        string? packaged = null;
        try
        {
            var package = Windows.ApplicationModel.Package.Current;
            packaged = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                package.Id.FamilyName,
                "LocalState");
        }
        catch
        {
        }

        if (packaged is not null)
            yield return packaged;

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "kusutori",
            "Tonarink");
    }

    private static IReadOnlyList<string> ListIpv4()
    {
        var addresses = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var item in nic.GetIPProperties().UnicastAddresses)
                {
                    if (item.Address.AddressFamily != AddressFamily.InterNetwork
                        || IPAddress.IsLoopback(item.Address))
                        continue;

                    var value = item.Address.ToString();
                    if (!addresses.Contains(value, StringComparer.Ordinal))
                        addresses.Add(value);
                }
            }
        }
        catch
        {
        }

        return addresses;
    }

    private static bool IsChinese(string? language) =>
        !string.IsNullOrWhiteSpace(language)
            ? language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            : WidgetNative.UserLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase);
}

internal static partial class WidgetNative
{
    public static string UserLocale()
    {
        Span<char> buffer = stackalloc char[85];
        int length;
        unsafe
        {
            fixed (char* pointer = buffer)
                length = GetUserDefaultLocaleName(pointer, buffer.Length);
        }

        return length <= 1 ? "en-US" : buffer[..(length - 1)].ToString();
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetUserDefaultLocaleName(char* lpLocaleName, int cchLocaleName);
}

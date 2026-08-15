using System.Text.Json;
using System.Text.Json.Serialization;

static class AppSettingsStore
{
    private static readonly string FilePath = Path.Combine(AppPlatform.DataDirectory, "settings.json");
    private static AppSettings? _cached;

    public static event Action? Changed;

    public static AppSettings Load()
    {
        if (_cached is { } cached)
            return cached;

        try
        {
            if (!File.Exists(FilePath))
                return _cached = AppSettings.Default;

            var json = File.ReadAllText(FilePath);
            var file = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettingsFile);
            return _cached = file?.ToSettings() ?? AppSettings.Default;
        }
        catch
        {
            return _cached = AppSettings.Default;
        }
    }

    public static void Save(AppSettings settings)
    {
        var previous = _cached;
        _cached = settings;
        Directory.CreateDirectory(AppPlatform.DataDirectory);
        var json = JsonSerializer.Serialize(AppSettingsFile.FromSettings(settings), AppSettingsJsonContext.Default.AppSettingsFile);
        File.WriteAllText(FilePath, json);
        if (previous != settings)
            Changed?.Invoke();
    }
}

sealed class AppSettingsFile
{
    public string? Alias { get; set; }
    public string? AutoSave { get; set; }
    public int? ThemeIndex { get; set; }
    public int? LanguageIndex { get; set; }
    public bool? MinimizeToTray { get; set; }
    public bool? StartWithWindows { get; set; }
    public bool? AnimationsEnabled { get; set; }
    public bool? FavoritesOnly { get; set; }
    public string? DownloadDirectory { get; set; }

    public static AppSettingsFile FromSettings(AppSettings settings) => new()
    {
        Alias = settings.Alias,
        AutoSave = settings.AutoSave.ToString(),
        ThemeIndex = settings.ThemeIndex,
        LanguageIndex = settings.LanguageIndex,
        MinimizeToTray = settings.MinimizeToTray,
        StartWithWindows = settings.StartWithWindows,
        AnimationsEnabled = settings.AnimationsEnabled,
        FavoritesOnly = settings.FavoritesOnly,
        DownloadDirectory = settings.DownloadDirectory,
    };

    public AppSettings ToSettings()
    {
        var defaults = AppSettings.Default;
        return defaults with
        {
            Alias = string.IsNullOrWhiteSpace(Alias) ? defaults.Alias : Alias.Trim(),
            AutoSave = Enum.TryParse<AutoSaveMode>(AutoSave, ignoreCase: true, out var autoSave)
                ? autoSave
                : defaults.AutoSave,
            ThemeIndex = ThemeIndex is >= 0 and <= 2 ? ThemeIndex.Value : defaults.ThemeIndex,
            LanguageIndex = LanguageIndex is >= 0 and <= 2 ? LanguageIndex.Value : defaults.LanguageIndex,
            MinimizeToTray = MinimizeToTray ?? defaults.MinimizeToTray,
            StartWithWindows = StartWithWindows ?? defaults.StartWithWindows,
            AnimationsEnabled = AnimationsEnabled ?? defaults.AnimationsEnabled,
            FavoritesOnly = FavoritesOnly ?? defaults.FavoritesOnly,
            DownloadDirectory = string.IsNullOrWhiteSpace(DownloadDirectory)
                ? defaults.DownloadDirectory
                : DownloadDirectory,
        };
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettingsFile))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;

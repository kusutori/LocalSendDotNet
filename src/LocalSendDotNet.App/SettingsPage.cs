using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using static LocalSendDotNet.App.Controls.Toolkit.SettingsCardElement;

sealed record SettingsPageProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings);

sealed class SettingsPage : Component<SettingsPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        string[] themeOptions =
        [
            t.Message(new("App", "OptionSystem")),
            t.Message(new("App", "ThemeLight")),
            t.Message(new("App", "ThemeDark")),
        ];
        string[] languageOptions =
        [
            t.Message(new("App", "OptionSystem")),
            t.Message(new("App", "LanguageChinese")),
            t.Message(new("App", "LanguageEnglish")),
        ];

        var generalCards = SettingsGroup(
            t.Message(new("App", "SettingsGeneral")),
            SettingsCard(
                header: t.Message(new("App", "SettingsTheme")),
                description: t.Message(new("App", "SettingsThemeDescription")),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ComboBox(themeOptions, Props.Settings.ThemeIndex, index =>
                {
                    if (index is >= 0 and <= 2 && index != Props.Settings.ThemeIndex)
                        Props.UpdateSettings(settings => settings with { ThemeIndex = index });
                })
                    .MinWidth(180)),
            SettingsCard(
                header: t.Message(new("App", "SettingsLanguage")),
                description: t.Message(new("App", "SettingsLanguageDescription")),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ComboBox(languageOptions, Props.Settings.LanguageIndex, index =>
                {
                    if (index is >= 0 and <= 2 && index != Props.Settings.LanguageIndex)
                        Props.UpdateSettings(settings => settings with { LanguageIndex = index });
                })
                    .MinWidth(180)),
            SettingsCard(
                header: t.Message(new("App", "SettingsMinimizeToTray")),
                description: t.Message(new("App", "SettingsMinimizeToTrayDescription")),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.MinimizeToTray, value =>
                    Props.UpdateSettings(settings => settings with { MinimizeToTray = value }))),
            SettingsCard(
                header: t.Message(new("App", "SettingsStartWithWindows")),
                description: t.Message(new("App", "SettingsStartWithWindowsDescription")),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.StartWithWindows, value =>
                    Props.UpdateSettings(settings => settings with { StartWithWindows = value }))),
            SettingsCard(
                header: t.Message(new("App", "SettingsAnimations")),
                description: t.Message(new("App", "SettingsAnimationsDescription")),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.AnimationsEnabled, value =>
                    Props.UpdateSettings(settings => settings with { AnimationsEnabled = value }))));

        var receiveCards = SettingsGroup(
            t.Message(new("App", "SettingsReceive")),
            SettingsCard(
                header: t.Message(new("App", "SettingsDeviceName")),
                description: t.Message(new("App", "SettingsDeviceNameDescription")),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                TextBox(Props.Settings.Alias, value =>
                    Props.UpdateSettings(settings => settings with { Alias = value }))
                    .Header(t.Message(new("App", "SettingsDeviceName")))
                    .AutomationName(t.Message(new("App", "SettingsDeviceName")))
                    .MinWidth(240)),
            SettingsCard(
                header: t.Message(new("App", "SettingsAutoSave")),
                description: t.Message(new("App", "SettingsAutoSaveDescription")),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.AutoSave == AutoSaveMode.On, value =>
                    Props.UpdateSettings(settings => settings with
                    {
                        AutoSave = value ? AutoSaveMode.On : AutoSaveMode.Off,
                    }))),
            SettingsCard(
                header: t.Message(new("App", "SettingsFavoritesOnly")),
                description: t.Message(new("App", "SettingsFavoritesOnlyDescription")),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.FavoritesOnly, value =>
                    Props.UpdateSettings(settings => settings with { FavoritesOnly = value }))),
            SettingsCard(
                header: t.Message(new("App", "SettingsSaveLocation")),
                description: Props.Settings.DownloadDirectory,
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                Button(t.Message(new("App", "Change")), () => { })
                    .AutomationName(t.Message(new("App", "ChangeSaveLocation")))));

        return ScrollView(
            VStack(24,
                Heading(t.Message(new("App", "SettingsTitle")))
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                generalCards,
                receiveCards)
            .Padding(36))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);
    }

    private static Element SettingsGroup(string title, params Element[] cards) =>
        VStack(4,
        [
            Subtitle(title)
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .Margin(bottom: 8),
            .. cards.Select(card => card.HAlign(HorizontalAlignment.Stretch)),
        ]);
}

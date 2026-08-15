using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using static Microsoft.UI.Reactor.Factories;
using static LocalSendDotNet.App.Controls.Toolkit.SettingsCardElement;

sealed record SettingsPageProps(
    AppSettings Settings,
    AppRuntimeState Runtime,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    Action StartOrRestartServer,
    Action StopServer);

sealed class SettingsPage : Component<SettingsPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var (statusMessage, setStatusMessage) = UseState<string?>(null);
        var nodeState = Props.Runtime.NodeState;
        var serverBusy = nodeState is LocalSendNodeState.Starting or LocalSendNodeState.Stopping;
        var serverRunning = nodeState == LocalSendNodeState.Running;
        var serverOnline = nodeState is LocalSendNodeState.Running or LocalSendNodeState.Starting;
        var needsRestart = serverRunning
            && Props.Runtime.Identity is { } identity
            && !string.Equals(identity.Alias, Props.Settings.ResolvedAlias, StringComparison.Ordinal);
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
                    _ = SetStartupAsync(value))),
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
                Button(t.Message(new("App", "Change")), () => _ = PickDownloadDirectoryAsync())
                    .AutomationName(t.Message(new("App", "ChangeSaveLocation")))));

        var startOrRestartName = serverOnline
            ? t.Message(new("App", "SettingsRestartServer"))
            : t.Message(new("App", "SettingsStartServer"));
        var stopName = t.Message(new("App", "SettingsStopServer"));
        var serverDescription = needsRestart
            ? t.Message(new("App", "SettingsNeedRestart"))
            : nodeState switch
            {
                LocalSendNodeState.Running => t.Message(new("App", "SettingsServerRunning")),
                LocalSendNodeState.Starting => t.Message(new("App", "NodeStarting")),
                LocalSendNodeState.Stopping => t.Message(new("App", "NodeStopping")),
                _ => t.Message(new("App", "SettingsServerStopped")),
            };
        var networkCards = SettingsGroup(
            t.Message(new("App", "SettingsNetwork")),
            SettingsCard(
                header: serverOnline
                    ? t.Message(new("App", "DeviceServer"))
                    : t.Message(new("App", "SettingsServerOffline")),
                description: serverDescription,
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                HStack(4,
                    Button(Icon(serverOnline ? "Refresh" : "Play"), Props.StartOrRestartServer)
                        .SubtleButton()
                        .AutomationName(startOrRestartName)
                        .ToolTip(startOrRestartName)
                        .IsEnabled(!serverBusy)
                        .MinWidth(40)
                        .MinHeight(40),
                    Button(Icon("Stop"), Props.StopServer)
                        .SubtleButton()
                        .AutomationName(stopName)
                        .ToolTip(stopName)
                        .IsEnabled(serverRunning && !serverBusy)
                        .MinWidth(40)
                        .MinHeight(40))),
            SettingsCard(
                header: t.Message(new("App", "SettingsDeviceName")),
                description: t.Message(new("App", "SettingsDeviceNameDescription")),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                TextBox(Props.Settings.Alias, value =>
                    Props.UpdateSettings(settings => settings with { Alias = value }))
                    .AutomationName(t.Message(new("App", "SettingsDeviceName")))
                    .MinWidth(240)));

        return ScrollView(
            VStack(24,
                Heading(t.Message(new("App", "SettingsTitle")))
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                statusMessage is null
                    ? null
                    : (InfoBar(t.Message(new("App", "SettingsTitle")), statusMessage) with
                    {
                        IsOpen = true,
                        IsClosable = true,
                        OnClosed = () => setStatusMessage(null),
                    }).Severity(InfoBarSeverity.Error),
                Props.Runtime.Error is null
                    ? null
                    : (InfoBar(t.Message(new("App", "NetworkStartFailed")), Props.Runtime.Error) with
                    {
                        IsOpen = true,
                        IsClosable = false,
                    }).Severity(InfoBarSeverity.Error),
                generalCards,
                receiveCards,
                networkCards)
            .Padding(36))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);

        async Task SetStartupAsync(bool enabled)
        {
            try
            {
                await WindowsStartup.SetEnabledAsync(enabled, Props.Settings.MinimizeToTray);
                Props.UpdateSettings(settings => settings with { StartWithWindows = enabled });
                setStatusMessage(null);
            }
            catch (StartupDisabledException exception)
            {
                setStatusMessage(t.Message(new("App", exception.ResourceKey)));
            }
            catch (Exception exception)
            {
                setStatusMessage(t.Message(
                    new("App", "StartupFailed"),
                    ("error", exception.Message)));
            }
        }

        async Task PickDownloadDirectoryAsync()
        {
            try
            {
                var picker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.Downloads,
                    CommitButtonText = t.Message(new("App", "Change")),
                };
                picker.FileTypeFilter.Add("*");
                var nativeWindow = window?.NativeWindow
                    ?? throw new InvalidOperationException(t.Message(new("App", "WindowUnavailable")));
                WinRT.Interop.InitializeWithWindow.Initialize(
                    picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow));
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null)
                    return;

                Props.UpdateSettings(settings => settings with { DownloadDirectory = folder.Path });
                setStatusMessage(null);
            }
            catch (Exception exception)
            {
                setStatusMessage(t.Message(
                    new("App", "PickFolderFailed"),
                    ("error", exception.Message)));
            }
        }
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

using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.Toolkit.SegmentedElement;
using static TransferOverlayVisuals;

sealed record ReceivePageProps(
    AppRuntimeState Runtime,
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings);

sealed class ReceivePage : Component<ReceivePageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var navigation = UseNavigation<AppRoute>();
        var autoSaveItems = UseMemo(() => new object[]
        {
            new SegmentedItem { Content = t.Message(new("App", "AutoSaveOff")) },
            new SegmentedItem { Content = t.Message(new("App", "AutoSaveFavorites")) },
            new SegmentedItem { Content = t.Message(new("App", "AutoSaveOn")) },
        }, t.Locale);
        var identity = Props.Runtime.Identity;
        var stored = AppSettingsStore.Load();
        var (alias, setAlias) = UseState(stored.ResolvedAlias);
        var idleLogoPlayerRef = UseRef<AnimatedVisualPlayer?>(null);
        UseNavigationLifecycle(onNavigatedTo: _ =>
        {
            var current = AppSettingsStore.Load();
            setAlias(current.ResolvedAlias);
            PlayIdleLogoAnimation(idleLogoPlayerRef.Current);
        });
        var fingerprint = identity?.Fingerprint;
        var fingerprintPreview = fingerprint is null ? null : fingerprint[..12];
        var shortId = fingerprint is null
            ? t.Message(new("App", "IdentityLoading"))
            : $"#{Convert.ToInt32(fingerprint[..4], 16) % 1000:D3}  #1";

        var identityPanel = FlexColumn(
            (AnimatedVisualPlayer() with { AutoPlay = false })
                .Size(144, 144)
                .HAlign(HorizontalAlignment.Center)
                .AccessibilityHidden()
                .OnMountAdd(element =>
                {
                    if (element is not AnimatedVisualPlayer player)
                        return;

                    idleLogoPlayerRef.Current = player;
                    PlayIdleLogoAnimation(player);
                })
                .OnUnmountAdd(element =>
                {
                    if (element is AnimatedVisualPlayer player
                        && ReferenceEquals(idleLogoPlayerRef.Current, player))
                    {
                        idleLogoPlayerRef.Current = null;
                    }
                }),
            Title(alias).HAlign(HorizontalAlignment.Center),
            BodyLarge(shortId)
                .Foreground(Theme.SecondaryText)
                .HAlign(HorizontalAlignment.Center),
            fingerprint is null
                ? null
                : Caption(t.Message(
                        new("App", "Fingerprint"),
                        ("fingerprint", fingerprintPreview!)))
                    .Foreground(Theme.TertiaryText)
                    .HAlign(HorizontalAlignment.Center)) with
        {
            RowGap = 12,
            AlignItems = FlexAlign.Center,
        };

        var autoSave = Card(
            FlexColumn(
                FlexRow(
                    VStack(4,
                        Subtitle(t.Message(new("App", "AutoSaveTitle"))),
                        TextBlock(t.Message(new("App", "AutoSaveDescription")))
                            .Foreground(Theme.SecondaryText))
                        .Flex(grow: 1, basis: 0),
                    Props.Runtime.IncomingTransfers.Count > 0
                        ? InfoBadge(Props.Runtime.IncomingTransfers.Count)
                            .AutomationName(t.Message(
                                new("App", "PendingRequests"),
                                ("count", Props.Runtime.IncomingTransfers.Count)))
                        : null) with
                {
                    AlignItems = FlexAlign.Center,
                    ColumnGap = 12,
                },
                Segmented(
                    selectedIndex: (int)Props.Settings.AutoSave,
                    onSelectedIndexChanged: index => Props.UpdateSettings(settings => settings with
                    {
                        AutoSave = (AutoSaveMode)index,
                        FavoritesOnly = (AutoSaveMode)index == AutoSaveMode.Favorites,
                    }),
                    items: autoSaveItems)
                    .HAlign(HorizontalAlignment.Stretch)) with
            { RowGap = 20 })
            .MaxWidth(560)
            .HAlign(HorizontalAlignment.Stretch);

        var page = ScrollView(
            FlexColumn(
                FlexRow(
                        Heading(t.Message(new("App", "ReceiveTitle")))
                            .HeadingLevel(AutomationHeadingLevel.Level1)
                            .Flex(grow: 1, basis: 0),
                        Button(Icon(FontIcon("\uE121")), () => navigation.Navigate(AppRoute.History))
                            .SubtleButton()
                            .AutomationName(t.Message(new("App", "HistoryOpenReceiveHistory")))
                            .MinWidth(40)
                            .MinHeight(40))
                    with
                { AlignItems = FlexAlign.Center, ColumnGap = 8 },
                identityPanel.Flex(grow: 1, basis: 0),
                autoSave) with
            {
                RowGap = 32,
                AlignItems = FlexAlign.Stretch,
            })
            .Padding(36)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);

        return page;
    }

    private static void PlayIdleLogoAnimation(AnimatedVisualPlayer? player)
    {
        if (player is null)
            return;

        player.Source = new Tonarink.IdleLogo();
        _ = player.PlayAsync(fromProgress: 0, toProgress: 1, looped: true);
    }
}

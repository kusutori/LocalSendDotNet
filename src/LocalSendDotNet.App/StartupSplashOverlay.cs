using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

sealed class StartupSplashOverlay : Component
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(280);

    public override Element Render()
    {
        var (opacity, setOpacity) = UseState(1.0);
        var alive = UseRef(true);

        UseEffect(() => () => { alive.Current = false; });

        return Border(
                (AnimatedVisualPlayer() with { AutoPlay = false })
                    .Size(256, 256)
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
                    .AutomationName("LocalSend")
                    .OnMountAdd(element =>
                    {
                        if (element is AnimatedVisualPlayer player)
                            _ = PlayThenFadeAsync(player);
                    }))
            .Background(Theme.SolidBackground)
            .Opacity(opacity)
            .OpacityTransition(FadeDuration)
            .IsHitTestVisible(opacity > 0);

        async Task PlayThenFadeAsync(AnimatedVisualPlayer player)
        {
            try
            {
                player.Source = new LocalSendDotNet.SplashLogo();
                await player.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
            }
            catch
            {
            }

            if (alive.Current)
                setOpacity(0);
        }
    }
}

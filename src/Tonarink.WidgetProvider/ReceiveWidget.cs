using System.Diagnostics;
using Microsoft.Windows.Widgets.Providers;

namespace Tonarink.WidgetProvider;

internal sealed class ReceiveWidget
{
    public const string DefinitionId = "Tonarink_Receive";
    private static readonly string TemplatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "ReceiveWidget.json");
    private static string? Template;

    public ReceiveWidget(string widgetId)
    {
        Id = widgetId;
    }

    public string Id { get; }

    public static string GetTemplate()
    {
        if (Template is not null)
            return Template;

        Template = File.Exists(TemplatePath)
            ? File.ReadAllText(TemplatePath)
            : """{"type":"AdaptiveCard","version":"1.5","body":[{"type":"TextBlock","text":"Tonarink","weight":"bolder"}]}""";
        return Template;
    }

    public static string GetData() => WidgetSnapshot.Capture().ToJson();

    public static void OpenApp()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "tonarink:",
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }
}

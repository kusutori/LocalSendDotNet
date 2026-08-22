using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;

namespace Tonarink.WidgetProvider;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid(ComServer.Clsid)]
public sealed class WidgetProvider : IWidgetProvider
{
    internal static readonly ManualResetEvent Idle = new(false);
    private static readonly Dictionary<string, ReceiveWidget> Instances = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static bool recovered;

    public WidgetProvider() => Recover();

    public void CreateWidget(WidgetContext widgetContext)
    {
        if (!string.Equals(widgetContext.DefinitionId, ReceiveWidget.DefinitionId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unknown widget '{widgetContext.DefinitionId}'.");

        lock (Gate)
        {
            Instances[widgetContext.Id] = new ReceiveWidget(widgetContext.Id);
            Idle.Reset();
        }

        Push(widgetContext.Id, includeTemplate: true);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        _ = customState;
        lock (Gate)
        {
            Instances.Remove(widgetId);
            if (Instances.Count == 0)
                Idle.Set();
        }
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        if (string.Equals(actionInvokedArgs.Verb, "open", StringComparison.Ordinal))
            ReceiveWidget.OpenApp();
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs) =>
        Push(contextChangedArgs.WidgetContext.Id, includeTemplate: false);

    public void Activate(WidgetContext widgetContext) =>
        Push(widgetContext.Id, includeTemplate: true);

    public void Deactivate(string widgetId) => _ = widgetId;

    private static void Push(string widgetId, bool includeTemplate)
    {
        lock (Gate)
        {
            if (!Instances.ContainsKey(widgetId))
                Instances[widgetId] = new ReceiveWidget(widgetId);
        }

        var options = new WidgetUpdateRequestOptions(widgetId)
        {
            Data = ReceiveWidget.GetData(),
            CustomState = string.Empty,
        };
        if (includeTemplate)
            options.Template = ReceiveWidget.GetTemplate();

        WidgetManager.GetDefault().UpdateWidget(options);
    }

    private static void Recover()
    {
        if (recovered)
            return;

        recovered = true;
        try
        {
            var infos = WidgetManager.GetDefault().GetWidgetInfos();
            if (infos is null)
                return;

            lock (Gate)
            {
                foreach (var info in infos)
                {
                    var context = info.WidgetContext;
                    if (!string.Equals(context.DefinitionId, ReceiveWidget.DefinitionId, StringComparison.Ordinal))
                    {
                        WidgetManager.GetDefault().DeleteWidget(context.Id);
                        continue;
                    }

                    Instances[context.Id] = new ReceiveWidget(context.Id);
                }

                if (Instances.Count == 0)
                    return;

                Idle.Reset();
            }

            foreach (var id in Instances.Keys.ToArray())
                Push(id, includeTemplate: true);
        }
        catch
        {
        }
    }
}

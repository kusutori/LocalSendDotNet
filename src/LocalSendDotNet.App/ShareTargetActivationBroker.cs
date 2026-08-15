using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

static class ShareTargetActivationBroker
{
    private const string InstanceKey = "LocalSendDotNet.Primary";
    private const string IngestedEventName = @"Local\LocalSendDotNet.ShareIngested";
    private static readonly ConcurrentQueue<ShareTargetPayload> PendingPayloads = new();
    private static readonly EventWaitHandle Ingested = new(
        initialState: true,
        mode: EventResetMode.ManualReset,
        name: IngestedEventName);

    public static event EventHandler? ActivationReceived;

    public static bool HasPendingActivations => !PendingPayloads.IsEmpty;

    public static async Task<bool> RedirectToPrimaryInstanceAsync()
    {
        if (!AppPlatform.HasPackageIdentity())
            return false;

        var current = AppInstance.GetCurrent();
        var primary = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!primary.IsCurrent)
        {
            Ingested.Reset();
            await primary.RedirectActivationToAsync(current.GetActivatedEventArgs());
            Ingested.WaitOne(TimeSpan.FromSeconds(15));
            return true;
        }

        primary.Activated += OnActivated;
        await IngestAsync(current.GetActivatedEventArgs()).ConfigureAwait(true);
        return false;
    }

    public static bool TryDequeue(out ShareTargetPayload? payload) =>
        PendingPayloads.TryDequeue(out payload);

    private static void OnActivated(object? sender, AppActivationArguments activation) =>
        _ = IngestAsync(activation);

    private static async Task IngestAsync(AppActivationArguments? activation)
    {
        try
        {
            if (activation?.Kind == ExtendedActivationKind.ShareTarget
                && activation.Data is ShareTargetActivatedEventArgs shareArgs)
            {
                var payload = await CaptureSharePayloadAsync(shareArgs).ConfigureAwait(false);
                if (payload is not null)
                    PendingPayloads.Enqueue(payload);
            }
        }
        catch
        {
        }
        finally
        {
            try
            {
                Ingested.Set();
            }
            catch
            {
            }

            ActivationReceived?.Invoke(null, EventArgs.Empty);
        }
    }

    private static Task<ShareTargetPayload?> CaptureSharePayloadAsync(
        ShareTargetActivatedEventArgs shareArgs)
    {
        var dispatcher = ReactorApp.UIDispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
            return CaptureSharePayloadCoreAsync(shareArgs);

        var completion = new TaskCompletionSource<ShareTargetPayload?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.High, () =>
                _ = CompleteOnDispatcherAsync(shareArgs, completion)))
        {
            return CaptureSharePayloadCoreAsync(shareArgs);
        }

        return completion.Task;
    }

    private static async Task CompleteOnDispatcherAsync(
        ShareTargetActivatedEventArgs shareArgs,
        TaskCompletionSource<ShareTargetPayload?> completion)
    {
        try
        {
            completion.TrySetResult(await CaptureSharePayloadCoreAsync(shareArgs).ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private static async Task<ShareTargetPayload?> CaptureSharePayloadCoreAsync(
        ShareTargetActivatedEventArgs shareArgs)
    {
        var operation = shareArgs.ShareOperation;
        operation.ReportStarted();
        try
        {
            var payload = await ReadShareTargetPayloadAsync(operation.Data).ConfigureAwait(true);
            operation.ReportDataRetrieved();
            operation.ReportCompleted();
            return payload;
        }
        catch
        {
            try
            {
                operation.ReportError("The shared content could not be read.");
            }
            catch
            {
            }

            throw;
        }
    }

    private static async Task<ShareTargetPayload> ReadShareTargetPayloadAsync(DataPackageView data)
    {
        var items = new List<ShareTargetItem>();
        if (data.Contains(StandardDataFormats.StorageItems))
        {
            var storageItems = await data.GetStorageItemsAsync();
            foreach (var storageItem in storageItems)
            {
                if (string.IsNullOrWhiteSpace(storageItem.Path))
                    continue;

                items.Add(new ShareTargetItem.FileSystem(
                    storageItem.Path,
                    storageItem is StorageFolder));
            }
        }
        else if (data.Contains(StandardDataFormats.WebLink))
        {
            var link = await data.GetWebLinkAsync();
            items.Add(new ShareTargetItem.Text(link.ToString(), "shared-link.txt"));
        }
        else if (data.Contains(StandardDataFormats.Text))
        {
            var text = await data.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
                items.Add(new ShareTargetItem.Text(text, "shared-text.txt"));
        }

        if (items.Count == 0)
            throw new InvalidDataException("The share did not contain accessible files or text.");

        return new ShareTargetPayload(Guid.NewGuid(), items);
    }
}

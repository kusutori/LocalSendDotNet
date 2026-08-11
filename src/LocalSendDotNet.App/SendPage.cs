using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using static Microsoft.UI.Reactor.Factories;

sealed record SendPageProps(
    AppRuntimeState Runtime,
    LocalSendNode? Node,
    Func<Task> RefreshAsync);

sealed record SelectedSendItem(
    Guid Id,
    SendItem Item,
    string DisplayName,
    long Length,
    string Kind);

sealed record SendRequest(
    LocalSendDevice Device,
    IReadOnlyList<SendItem> Items,
    string? Pin,
    CancellationToken CancellationToken);

sealed record TransferUiState(
    TransferState? State,
    string? DeviceName,
    long BytesTransferred,
    long TotalBytes,
    string Message,
    bool IsError)
{
    public static readonly TransferUiState Idle = new(
        State: null,
        DeviceName: null,
        BytesTransferred: 0,
        TotalBytes: 0,
        Message: "选择内容后，点击附近设备即可发送。",
        IsError: false);
}

sealed class SendPage : Component<SendPageProps>
{
    public override Element Render()
    {
        var window = UseWindow();
        var (selectedItems, updateSelectedItems) = UseReducer<IReadOnlyList<SelectedSendItem>>(
            Array.Empty<SelectedSendItem>());
        var (pickerMessage, setPickerMessage) = UseState("尚未选择内容");
        var (text, setText) = UseState(string.Empty);
        var (showTextDialog, setShowTextDialog) = UseState(false);
        var (pinTarget, setPinTarget) = UseState<LocalSendDevice?>(null);
        var (pin, setPin) = UseState(string.Empty);
        var (pinError, setPinError) = UseState<string?>(null);
        var (transfer, updateTransfer) = UseReducer(TransferUiState.Idle);
        var sendCancellationRef = UseRef<CancellationTokenSource?>(null);

        var sendMutation = UseMutation<SendRequest, TransferResult>(async (request, mutationToken) =>
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                request.CancellationToken,
                mutationToken);
            var progress = new Progress<TransferProgress>(value =>
                updateTransfer(_ => new(
                    value.State,
                    request.Device.Alias,
                    value.BytesTransferred,
                    value.TotalBytes,
                    ProgressMessage(value.State, request.Device.Alias),
                    IsError: false)));

            return await Props.Node!.SendAsync(
                request.Device,
                request.Items,
                new SendOptions { Pin = request.Pin },
                progress,
                linkedCancellation.Token).ConfigureAwait(false);
        });

        var selectionGrid = Grid(
            columns:
            [
                GridSize.Star(),
                GridSize.Star(),
                GridSize.Star(),
                GridSize.Star(),
            ],
            rows: [GridSize.Auto],
            SelectionTile("文件", "Document", () => _ = PickFileAsync())
                .Grid(column: 0),
            SelectionTile("文件夹", "Folder", () => _ = PickFolderAsync())
                .Grid(column: 1),
            SelectionTile("文本", "Edit", () => setShowTextDialog(true))
                .Grid(column: 2),
            SelectionTile("剪贴板", "Paste", () => _ = AddClipboardAsync())
                .Grid(column: 3)) with
        {
            ColumnSpacing = 12,
        };

        Element selectedContent = selectedItems.Count == 0
            ? Caption(pickerMessage).Foreground(Theme.SecondaryText)
            : Card(
                VStack(8,
                    FlexRow(
                        BodyStrong($"已选择 {selectedItems.Count} 项 · {FormatBytes(selectedItems.Sum(static item => item.Length))}")
                            .Flex(grow: 1, basis: 0),
                        Button("清空", () =>
                        {
                            updateSelectedItems(_ => Array.Empty<SelectedSendItem>());
                            setPickerMessage("尚未选择内容");
                        })) with
                    {
                        AlignItems = FlexAlign.Center,
                        ColumnGap = 8,
                    },
                    ScrollView(
                        VStack(4,
                            selectedItems.Select(item => SelectedItemRow(
                                item,
                                () => updateSelectedItems(current =>
                                    current.Where(candidate => candidate.Id != item.Id).ToArray()))
                                .WithKey(item.Id.ToString("N")))
                            .ToArray<Element?>()))
                        .MaxHeight(156)
                        .HorizontalContentAlignment(HorizontalAlignment.Stretch)));

        var devices = Props.Runtime.Devices;
        Element deviceContent = devices.Count == 0
            ? EmptyDevices(Props.Runtime.NodeState)
            : VStack(8,
                devices.Select((device, index) =>
                    DeviceCard(
                        device,
                        isEnabled: selectedItems.Count > 0
                            && Props.Node?.State == LocalSendNodeState.Running
                            && !sendMutation.IsPending,
                        onClick: () => _ = StartSendAsync(device, pin: null))
                        .PositionInSet(index + 1, devices.Count)
                        .WithKey(device.Fingerprint))
                .ToArray<Element?>());

        var page = FlexColumn(
            Heading("发送")
                .HeadingLevel(AutomationHeadingLevel.Level1),
            VStack(12,
                Subtitle("选择内容")
                    .HeadingLevel(AutomationHeadingLevel.Level2),
                selectionGrid,
                selectedContent),
            TransferPanel(
                transfer,
                sendMutation.IsPending,
                () =>
                {
                    sendCancellationRef.Current?.Cancel();
                    updateTransfer(current => current with { Message = "正在取消传输…" });
                }),
            FlexRow(
                Subtitle("附近的设备")
                    .HeadingLevel(AutomationHeadingLevel.Level2)
                    .Flex(grow: 1, basis: 0),
                Button(Icon("Refresh"), () => _ = Props.RefreshAsync())
                    .AutomationName("刷新附近设备")
                    .ToolTip("刷新附近设备")
                    .IsEnabled(!sendMutation.IsPending)) with
            {
                AlignItems = FlexAlign.Center,
                ColumnGap = 8,
            },
            ScrollView(deviceContent)
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .Flex(grow: 1, basis: 0),
            TextDialog(),
            PinDialog());

        return Border(page)
            .Padding(36)
            .Landmark(AutomationLandmarkType.Main);

        Element TextDialog() => ContentDialog(
            "发送文本",
            TextBox(text, setText, placeholderText: "输入要发送的文本…")
                .Header("文本内容")
                .AutomationName("文本内容")
                .AcceptsReturn()
                .TextWrapping(TextWrapping.Wrap)
                .MinHeight(160),
            primaryButtonText: "添加") with
        {
            IsOpen = showTextDialog,
            SecondaryButtonText = "取消",
            OnClosed = result =>
            {
                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(text))
                {
                    var item = new SendTextItem(text);
                    AddSelectedItems([new(Guid.NewGuid(), item, "文本消息", TextLength(text), "文本")]);
                    setText(string.Empty);
                }
                setShowTextDialog(false);
            },
        };

        Element PinDialog() => ContentDialog(
            "需要 PIN",
            VStack(8,
                TextBlock($"{pinTarget?.Alias ?? "目标设备"} 要求输入接收 PIN。")
                    .TextWrapping(TextWrapping.WrapWholeWords),
                PasswordBox(pin, setPin, placeholderText: "输入 PIN")
                    .Header("PIN")
                    .AutomationName("PIN")
                    .MaxLength(32),
                pinError is null
                    ? null
                    : TextBlock(pinError).Foreground(Theme.SystemCritical)),
            primaryButtonText: "重试") with
        {
            IsOpen = pinTarget is not null,
            SecondaryButtonText = "取消",
            OnClosed = result =>
            {
                var target = pinTarget;
                setPinTarget(null);
                if (result == ContentDialogResult.Primary
                    && target is not null
                    && !string.IsNullOrWhiteSpace(pin))
                {
                    var retryPin = pin;
                    setPin(string.Empty);
                    setPinError(null);
                    _ = StartSendAsync(target, retryPin);
                }
                else
                {
                    setPin(string.Empty);
                    setPinError(null);
                }
            },
        };

        async Task PickFileAsync()
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.Downloads,
                    CommitButtonText = "添加",
                };
                picker.FileTypeFilter.Add("*");
                InitializePicker(picker);
                var files = await picker.PickMultipleFilesAsync();
                if (files.Count == 0)
                    return;

                var selected = new List<SelectedSendItem>(files.Count);
                foreach (var file in files)
                    selected.Add(await FromStorageFileAsync(file, file.Name, CancellationToken.None));
                AddSelectedItems(selected);
            }
            catch (Exception exception)
            {
                setPickerMessage($"无法选择文件：{exception.Message}");
            }
        }

        async Task PickFolderAsync()
        {
            try
            {
                var picker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.Downloads,
                    CommitButtonText = "添加文件夹",
                };
                picker.FileTypeFilter.Add("*");
                InitializePicker(picker);
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null)
                    return;

                var selected = await FromFolderAsync(folder, CancellationToken.None);
                if (selected.Count == 0)
                {
                    setPickerMessage("所选文件夹中没有可发送的文件。");
                    return;
                }
                AddSelectedItems(selected);
            }
            catch (Exception exception)
            {
                setPickerMessage($"无法选择文件夹：{exception.Message}");
            }
        }

        void InitializePicker(object picker)
        {
            var nativeWindow = window?.NativeWindow
                ?? throw new InvalidOperationException("当前 Reactor 窗口不可用。");
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        }

        async Task AddClipboardAsync()
        {
            try
            {
                var data = Clipboard.GetContent();
                if (data.Contains(StandardDataFormats.StorageItems))
                {
                    var storageItems = await data.GetStorageItemsAsync();
                    var selected = new List<SelectedSendItem>();
                    foreach (var storageItem in storageItems)
                    {
                        switch (storageItem)
                        {
                            case StorageFile file:
                                selected.Add(await FromStorageFileAsync(file, file.Name, CancellationToken.None));
                                break;
                            case StorageFolder folder:
                                selected.AddRange(await FromFolderAsync(folder, CancellationToken.None));
                                break;
                        }
                    }
                    if (selected.Count > 0)
                    {
                        AddSelectedItems(selected);
                        return;
                    }
                }

                if (data.Contains(StandardDataFormats.Text))
                {
                    var clipboardText = await data.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(clipboardText))
                    {
                        var item = new SendTextItem(clipboardText, "clipboard.txt");
                        AddSelectedItems([new(
                            Guid.NewGuid(),
                            item,
                            "剪贴板文本",
                            TextLength(clipboardText),
                            "剪贴板")]);
                        return;
                    }
                }

                if (data.Contains(StandardDataFormats.Bitmap))
                {
                    AddSelectedItems([await FromClipboardBitmapAsync(data, CancellationToken.None)]);
                    return;
                }

                setPickerMessage("剪贴板中没有可发送的文本、文件或文件夹。");
            }
            catch (Exception exception)
            {
                setPickerMessage($"无法读取剪贴板：{exception.Message}");
            }
        }

        void AddSelectedItems(IReadOnlyCollection<SelectedSendItem> newItems)
        {
            updateSelectedItems(current => [.. current, .. newItems]);
            setPickerMessage($"已添加 {newItems.Count} 项");
            updateTransfer(_ => TransferUiState.Idle);
        }

        async Task StartSendAsync(LocalSendDevice device, string? pin)
        {
            if (Props.Node?.State != LocalSendNodeState.Running || selectedItems.Count == 0)
                return;

            var cancellation = new CancellationTokenSource();
            sendCancellationRef.Current?.Dispose();
            sendCancellationRef.Current = cancellation;
            updateTransfer(_ => new(
                TransferState.Preparing,
                device.Alias,
                0,
                selectedItems.Sum(static item => item.Length),
                $"正在准备发送给 {device.Alias}…",
                IsError: false));

            try
            {
                var result = await sendMutation.RunAsync(new(
                    device,
                    selectedItems.Select(static item => item.Item).ToArray(),
                    pin,
                    cancellation.Token));
                updateTransfer(_ => ResultState(
                    result,
                    device.Alias,
                    selectedItems.Sum(static item => item.Length)));
                if (result.IsSuccess)
                {
                    updateSelectedItems(_ => Array.Empty<SelectedSendItem>());
                    setPickerMessage("尚未选择内容");
                }
            }
            catch (PinRequiredException exception)
            {
                setPinError(exception.InvalidPin ? "PIN 不正确，请重试。" : null);
                setPinTarget(device);
                updateTransfer(current => current with
                {
                    State = TransferState.WaitingForAcceptance,
                    Message = "目标设备要求 PIN。",
                    IsError = exception.InvalidPin,
                });
            }
            catch (PinRateLimitedException)
            {
                updateTransfer(current => current with
                {
                    State = TransferState.Failed,
                    Message = "PIN 尝试次数过多，请稍后再试。",
                    IsError = true,
                });
            }
            catch (Exception exception)
            {
                updateTransfer(current => current with
                {
                    State = TransferState.Failed,
                    Message = exception.Message,
                    IsError = true,
                });
            }
            finally
            {
                if (ReferenceEquals(sendCancellationRef.Current, cancellation))
                    sendCancellationRef.Current = null;
                cancellation.Dispose();
            }
        }
    }

    private static Element SelectionTile(string label, string icon, Action onClick) =>
        Button(
            VStack(8,
                Icon(icon).AccessibilityHidden(),
                BodyStrong(label)),
            onClick)
        .MinHeight(104)
        .HAlign(HorizontalAlignment.Stretch)
        .AutomationName($"选择{label}");

    private static Element SelectedItemRow(SelectedSendItem item, Action remove) =>
        Grid(
            columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Icon(ItemIcon(item.Kind)).AccessibilityHidden()
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 0),
            VStack(2,
                TextBlock(item.DisplayName)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .ToolTip(item.DisplayName),
                Caption($"{item.Kind} · {FormatBytes(item.Length)}")
                    .Foreground(Theme.SecondaryText))
                .Margin(horizontal: 12, vertical: 0)
                .Grid(column: 1),
            Button(Icon("Delete"), remove)
                .AutomationName($"移除 {item.DisplayName}")
                .ToolTip("移除")
                .Grid(column: 2))
        .Padding(8);

    private static Element DeviceCard(LocalSendDevice device, bool isEnabled, Action onClick) =>
        Button(
            Grid(
                columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                Border(Icon(DeviceIcon(device.DeviceType)).AccessibilityHidden())
                    .Size(56, 56)
                    .CornerRadius(28)
                    .Background(Theme.SubtleFill)
                    .Grid(column: 0),
                VStack(4,
                    BodyLarge(device.Alias),
                    Caption(DeviceDescription(device)).Foreground(Theme.SecondaryText))
                    .Margin(horizontal: 16, vertical: 0)
                    .VAlign(VerticalAlignment.Center)
                    .Grid(column: 1),
                Icon("Forward").AccessibilityHidden()
                    .VAlign(VerticalAlignment.Center)
                    .Grid(column: 2)),
            onClick)
        .MinHeight(88)
        .HAlign(HorizontalAlignment.Stretch)
        .AutomationName($"向 {device.Alias} 发送")
        .IsEnabled(isEnabled);

    private static Element TransferPanel(TransferUiState transfer, bool isPending, Action cancel)
    {
        if (transfer.State is null)
            return Caption(transfer.Message).Foreground(Theme.SecondaryText);

        var progress = transfer.TotalBytes <= 0
            ? 0
            : Math.Clamp(transfer.BytesTransferred * 100d / transfer.TotalBytes, 0, 100);
        return Card(
            VStack(12,
                FlexRow(
                    VStack(4,
                        BodyStrong(TransferTitle(transfer.State.Value)),
                        TextBlock(transfer.Message)
                            .Foreground(transfer.IsError ? Theme.SystemCritical : Theme.SecondaryText))
                        .Flex(grow: 1, basis: 0),
                    isPending
                        ? Button("取消", cancel)
                            .AutomationName("取消当前传输")
                        : null) with
                {
                    AlignItems = FlexAlign.Center,
                    ColumnGap = 12,
                },
                transfer.State is TransferState.Preparing or TransferState.WaitingForAcceptance
                    ? ProgressIndeterminate()
                    : Progress(progress),
                Caption($"{FormatBytes(transfer.BytesTransferred)} / {FormatBytes(transfer.TotalBytes)}")
                    .Foreground(Theme.SecondaryText)));
    }

    private static Element EmptyDevices(LocalSendNodeState state) =>
        FlexColumn(
            Icon(state == LocalSendNodeState.Faulted ? "Important" : "Find").AccessibilityHidden(),
            Subtitle(state == LocalSendNodeState.Faulted ? "无法启动网络服务" : "正在寻找附近设备"),
            TextBlock(state == LocalSendNodeState.Faulted
                    ? "请检查 53317 端口是否被其他 LocalSend 实例占用。"
                    : "请确保目标设备连接到同一个 Wi-Fi 网络。")
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.WrapWholeWords)) with
        {
            RowGap = 12,
            AlignItems = FlexAlign.Center,
            JustifyContent = FlexJustify.Center,
        };

    private static async Task<SelectedSendItem> FromStorageFileAsync(
        StorageFile file,
        string protocolName,
        CancellationToken cancellationToken)
    {
        var properties = await file.GetBasicPropertiesAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var item = new SendStreamItem(
            protocolName.Replace('\\', '/'),
            checked((long)properties.Size),
            async token =>
            {
                token.ThrowIfCancellationRequested();
                return await file.OpenStreamForReadAsync().ConfigureAwait(false);
            });
        return new(Guid.NewGuid(), item, protocolName, checked((long)properties.Size), "文件");
    }

    private static Task<IReadOnlyList<SelectedSendItem>> FromFolderAsync(
        StorageFolder folder,
        CancellationToken cancellationToken) => Task.Run<IReadOnlyList<SelectedSendItem>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(folder.Path))
                throw new IOException("所选文件夹没有可访问的本地路径。");

            return Directory.EnumerateFiles(folder.Path, "*", SearchOption.AllDirectories)
                .Select(path =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativeName = Path.GetRelativePath(folder.Path, path).Replace('\\', '/');
                    var protocolName = $"{folder.Name}/{relativeName}";
                    return new SelectedSendItem(
                        Guid.NewGuid(),
                        new SendFileItem(path, protocolName),
                        protocolName,
                        new FileInfo(path).Length,
                        "文件夹");
                })
                .ToArray();
        }, cancellationToken);

    private static async Task<SelectedSendItem> FromClipboardBitmapAsync(
        DataPackageView data,
        CancellationToken cancellationToken)
    {
        var reference = await data.GetBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
        using var probe = await reference.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var length = checked((long)probe.Size);
        var contentType = string.IsNullOrWhiteSpace(probe.ContentType) ? "image/png" : probe.ContentType;
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/bmp" => ".bmp",
            "image/gif" => ".gif",
            _ => ".png",
        };
        var fileName = $"clipboard-image{extension}";
        var item = new SendStreamItem(
            fileName,
            length,
            async token =>
            {
                var stream = await reference.OpenReadAsync().AsTask(token).ConfigureAwait(false);
                return stream.AsStreamForRead();
            },
            contentType);
        return new(Guid.NewGuid(), item, "剪贴板图片", length, "剪贴板");
    }

    private static TransferUiState ResultState(
        TransferResult result,
        string deviceAlias,
        long requestedBytes) => result.State switch
        {
            TransferState.Completed => new(
                result.State,
                deviceAlias,
                result.BytesTransferred,
                result.BytesTransferred,
                $"已成功发送给 {deviceAlias}。",
                IsError: false),
            TransferState.Cancelled => new(
                result.State,
                deviceAlias,
                result.BytesTransferred,
                requestedBytes,
                "传输已取消。",
                IsError: false),
            _ => new(
                result.State,
                deviceAlias,
                result.BytesTransferred,
                requestedBytes,
                result.Failure?.Message ?? "传输失败。",
                IsError: true),
        };

    private static string ProgressMessage(TransferState state, string deviceAlias) => state switch
    {
        TransferState.Preparing => $"正在准备发送给 {deviceAlias}…",
        TransferState.WaitingForAcceptance => $"正在等待 {deviceAlias} 接受…",
        TransferState.Transferring => $"正在发送给 {deviceAlias}…",
        TransferState.Completed => $"已成功发送给 {deviceAlias}。",
        TransferState.Cancelled => "传输已取消。",
        _ => "传输失败。",
    };

    private static string TransferTitle(TransferState state) => state switch
    {
        TransferState.Preparing => "正在准备",
        TransferState.WaitingForAcceptance => "等待接受",
        TransferState.Transferring => "正在传输",
        TransferState.Completed => "发送完成",
        TransferState.Cancelled => "已取消",
        _ => "发送失败",
    };

    private static string DeviceIcon(LocalSendDeviceType type) => type switch
    {
        LocalSendDeviceType.Mobile => "Phone",
        LocalSendDeviceType.Web => "World",
        LocalSendDeviceType.Server => "World",
        _ => "Remote",
    };

    private static string ItemIcon(string kind) => kind switch
    {
        "文本" => "Edit",
        "剪贴板" => "Paste",
        "文件夹" => "Folder",
        _ => "Document",
    };

    private static string DeviceDescription(LocalSendDevice device)
    {
        var model = string.IsNullOrWhiteSpace(device.DeviceModel) ? "未知设备" : device.DeviceModel;
        return $"{model}  ·  v{device.ProtocolVersion}";
    }

    private static long TextLength(string value) => System.Text.Encoding.UTF8.GetByteCount(value);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}

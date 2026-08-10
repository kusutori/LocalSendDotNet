using LocalSendDotNet.Protocol.V2;

namespace LocalSendDotNet.Protocol;

internal interface ILocalSendProtocolAdapter
{
    string Version { get; }
    DeviceInfoDto CreateDeviceInfo(DeviceIdentity identity, LocalSendOptions options, bool announce = false);
    LocalSendDevice ToPublicDevice(DeviceInfoDto dto, DeviceEndpoint endpoint, DateTimeOffset seenAt);
}

internal sealed class V2ProtocolAdapter : ILocalSendProtocolAdapter
{
    public string Version => V2Constants.Version;

    public DeviceInfoDto CreateDeviceInfo(DeviceIdentity identity, LocalSendOptions options, bool announce = false) => new()
    {
        Alias = options.Alias,
        Version = Version,
        DeviceModel = options.DeviceModel,
        DeviceType = options.DeviceType.ToString().ToLowerInvariant(),
        Fingerprint = identity.Fingerprint,
        Port = options.Port,
        Protocol = options.EnableHttps ? "https" : "http",
        Download = false,
        Announce = announce
    };

    public LocalSendDevice ToPublicDevice(DeviceInfoDto dto, DeviceEndpoint endpoint, DateTimeOffset seenAt) => new(
        dto.Alias,
        dto.Version,
        dto.DeviceModel,
        ParseDeviceType(dto.DeviceType),
        dto.Fingerprint,
        dto.Download,
        [endpoint],
        seenAt);

    internal static LocalSendDeviceType ParseDeviceType(string? value) => value?.ToLowerInvariant() switch
    {
        "mobile" => LocalSendDeviceType.Mobile,
        "web" => LocalSendDeviceType.Web,
        "headless" => LocalSendDeviceType.Headless,
        "server" => LocalSendDeviceType.Server,
        _ => LocalSendDeviceType.Desktop
    };
}

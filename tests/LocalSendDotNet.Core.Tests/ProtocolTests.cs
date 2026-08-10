using System.Text.Json;
using LocalSendDotNet.Protocol;
using LocalSendDotNet.Protocol.V2;

namespace LocalSendDotNet.Core.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void AnnouncementUsesV22CamelCaseShape()
    {
        var dto = new DeviceInfoDto
        {
            Alias = "测试设备",
            Version = "2.2",
            DeviceModel = null,
            DeviceType = "desktop",
            Fingerprint = new string('A', 64),
            Port = 53317,
            Protocol = "https",
            Announce = true
        };

        var json = JsonSerializer.Serialize(dto, V2Json.Options);

        Assert.Contains("\"deviceType\":\"desktop\"", json, StringComparison.Ordinal);
        Assert.Contains("\"announce\":true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deviceModel", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterPayloadOmitsFalseAnnounceExtension()
    {
        var dto = new DeviceInfoDto { Alias = "a", Version = "2.2", Fingerprint = "f", Port = 1, Protocol = "https" };
        var json = JsonSerializer.Serialize(dto, V2Json.Options);
        Assert.DoesNotContain("announce", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FileSizeIsDeserializedAsInt64()
    {
        const string json = """
            {"id":"x","fileName":"large.bin","size":5368709120,"fileType":"application/octet-stream"}
            """;
        var dto = JsonSerializer.Deserialize<FileDto>(json, V2Json.Options);
        Assert.Equal(5_368_709_120L, dto!.Size);
    }

    [Theory]
    [InlineData("mobile", LocalSendDeviceType.Mobile)]
    [InlineData("fridge", LocalSendDeviceType.Desktop)]
    [InlineData(null, LocalSendDeviceType.Desktop)]
    public void UnknownDeviceTypeFallsBackToDesktop(string? value, LocalSendDeviceType expected) =>
        Assert.Equal(expected, V2ProtocolAdapter.ParseDeviceType(value));

    [Fact]
    public void InvalidJsonIsRejected() => Assert.Throws<JsonException>(() =>
        JsonSerializer.Deserialize<DeviceInfoDto>("{not-json}", V2Json.Options));
}

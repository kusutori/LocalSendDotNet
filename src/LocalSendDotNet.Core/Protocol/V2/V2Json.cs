using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalSendDotNet.Protocol.V2;

internal static class V2Json
{
    public static JsonSerializerOptions Options => V2JsonContext.Default.Options;
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.Strict,
    WriteIndented = false)]
[JsonSerializable(typeof(DeviceInfoDto))]
[JsonSerializable(typeof(RegisterResponseDto))]
[JsonSerializable(typeof(FileDto))]
[JsonSerializable(typeof(PrepareUploadRequestDto))]
[JsonSerializable(typeof(PrepareUploadResponseDto))]
[JsonSerializable(typeof(ErrorResponseDto))]
internal sealed partial class V2JsonContext : JsonSerializerContext
{
}

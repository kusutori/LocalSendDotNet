using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalSendDotNet.Protocol.V2;

internal static class V2Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.Strict,
        WriteIndented = false
    };
}

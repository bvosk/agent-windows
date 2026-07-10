using System.Text.Json.Serialization;

namespace AgentWindows.Core.Protocol.Transport;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(DaemonRequest))]
[JsonSerializable(typeof(DaemonResponse))]
public sealed partial class ProtocolJsonContext : JsonSerializerContext;

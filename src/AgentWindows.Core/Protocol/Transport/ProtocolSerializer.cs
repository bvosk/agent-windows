using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AgentWindows.Core.Protocol.Transport;

/// <summary>Newline-delimited JSON encoding for the daemon named-pipe protocol.</summary>
public static class ProtocolSerializer
{
    // Relaxed escaping keeps element names and key chords readable in agent-facing
    // output; the JSON never crosses a trust boundary (local pipe and stdout only).
    private static readonly JsonSerializerOptions _options = new(
        ProtocolJsonContext.Default.Options
    )
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly ProtocolJsonContext _context = new(_options);

    public static string SerializeRequest(DaemonRequest request) =>
        JsonSerializer.Serialize(request, _context.DaemonRequest);

    public static string SerializeResponse(DaemonResponse response) =>
        JsonSerializer.Serialize(response, _context.DaemonResponse);

    public static DaemonRequest? DeserializeRequest(string line) =>
        TryDeserialize(line, _context.DaemonRequest);

    public static DaemonResponse? DeserializeResponse(string line) =>
        TryDeserialize(line, _context.DaemonResponse);

    private static T? TryDeserialize<T>(string line, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(line, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // Polymorphic payloads without a recognized discriminator land here.
            return null;
        }
    }
}

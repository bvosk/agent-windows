using System.Text.Encodings.Web;
using System.Text.Json;

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

    public static string SerializeRequest(DaemonRequest request) =>
        JsonSerializer.Serialize(request, _options);

    public static string SerializeResponse(DaemonResponse response) =>
        JsonSerializer.Serialize(response, _options);

    public static DaemonRequest? DeserializeRequest(string line) =>
        TryDeserialize<DaemonRequest>(line);

    public static DaemonResponse? DeserializeResponse(string line) =>
        TryDeserialize<DaemonResponse>(line);

    private static T? TryDeserialize<T>(string line)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line, _options);
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

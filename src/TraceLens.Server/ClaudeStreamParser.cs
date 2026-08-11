using System.Text.Json;

namespace TraceLens.Server;

internal static class ClaudeStreamParser
{
    public static string? TryGetText(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) &&
                type.GetString() == "stream_event" &&
                root.TryGetProperty("event", out var streamEvent) &&
                streamEvent.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("type", out var deltaType) &&
                deltaType.GetString() == "text_delta" &&
                delta.TryGetProperty("text", out var deltaText))
            {
                return deltaText.GetString();
            }

        }
        catch (JsonException)
        {
            return line;
        }

        return null;
    }
}

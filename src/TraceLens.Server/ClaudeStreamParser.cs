using System.Text.Json;

namespace TraceLens.Server;

/// <param name="Kind">delta(응답 조각) 또는 log(사람이 읽을 실행 로그).</param>
internal sealed record ParsedLine(string Kind, string Text);

internal static class ClaudeStreamParser
{
    /// <summary>
    /// stdout 한 줄을 읽는다. 응답 조각이면 delta, JSON이 아니거나 다른 형태면 log다.
    /// 예전에는 파싱 실패 줄을 그대로 답변 본문으로 보여 줘서 진단 출력이 답변에 섞였다.
    /// </summary>
    public static ParsedLine? Parse(string line)
    {
        var text = TryGetText(line);
        if (text is null)
        {
            return null;
        }

        return IsJson(line)
            ? new ParsedLine("delta", text)
            : new ParsedLine("log", text);
    }

    private static bool IsJson(string line)
    {
        try
        {
            using var _ = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

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

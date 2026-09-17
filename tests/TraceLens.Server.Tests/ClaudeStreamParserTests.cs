using Xunit;

namespace TraceLens.Server.Tests;

public sealed class ClaudeStreamParserTests
{
    [Fact]
    public void TryGetText_ReturnsPartialTextDelta()
    {
        const string line =
            "{\"type\":\"stream_event\",\"event\":{\"delta\":{\"type\":\"text_delta\",\"text\":\"설명\"}}}";

        var result = ClaudeStreamParser.TryGetText(line);

        Assert.Equal("설명", result);
    }

    [Fact]
    public void TryGetText_IgnoresNonTextJsonEvents()
    {
        var result = ClaudeStreamParser.TryGetText("{\"type\":\"system\",\"subtype\":\"init\"}");

        Assert.Null(result);
    }

    [Fact]
    public void TryGetText_IgnoresFinalResultToAvoidRepeatingPartialText()
    {
        var result = ClaudeStreamParser.TryGetText("{\"type\":\"result\",\"result\":\"전체 응답\"}");

        Assert.Null(result);
    }

    /// <summary>JSON이 아닌 stdout 줄은 답변 본문이 아니라 로그로 보낸다.</summary>
    [Fact]
    public void Parse_TreatsNonJsonLineAsLog()
    {
        var parsed = ClaudeStreamParser.Parse("Loading MCP server tracelens...");

        Assert.NotNull(parsed);
        Assert.Equal("log", parsed!.Kind);
        Assert.Equal("Loading MCP server tracelens...", parsed.Text);
    }

    [Fact]
    public void Parse_TreatsTextDeltaAsAnswer()
    {
        var parsed = ClaudeStreamParser.Parse(
            "{\"type\":\"stream_event\",\"event\":{\"delta\":{\"type\":\"text_delta\",\"text\":\"답\"}}}");

        Assert.NotNull(parsed);
        Assert.Equal("delta", parsed!.Kind);
        Assert.Equal("답", parsed.Text);
    }
}

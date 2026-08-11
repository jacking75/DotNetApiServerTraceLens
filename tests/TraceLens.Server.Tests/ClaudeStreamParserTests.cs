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
}

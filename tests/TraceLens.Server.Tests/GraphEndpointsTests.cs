using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Server.Tests;

public sealed class GraphEndpointsTests
{
    [Fact]
    public void GraphDocument_ReadsSchemaV1GraphWithoutClassesAsEmptyList()
    {
        var graph = new GraphDocument(
            1,
            "C:/Fixture/Fixture.sln",
            DateTimeOffset.Parse("2026-08-11T00:00:00+09:00"),
            [],
            null!,
            [],
            [],
            new Dictionary<string, string>());

        Assert.Empty(graph.Classes);
    }
}

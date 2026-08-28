using TraceLens.Analyzer.Models;
using TraceLens.Server.Endpoints;
using Xunit;

namespace TraceLens.Server.Tests;

public sealed class GraphEndpointsTests
{
    [Fact]
    public void GraphClassSummary_KeepsMenuFieldsAndReplacesMethodIdsWithCount()
    {
        var graphClass = new GraphClass(
            "Fixture.Services.OrderService",
            "OrderService",
            "Fixture.Services",
            NodeKind.Service,
            "Fixture/Services/OrderService.cs",
            5,
            40,
            ["Fixture.Services.OrderService.CreateAsync(int)", "Fixture.Services.OrderService.CancelAsync(int)"]);

        var summary = GraphClassSummary.From(graphClass);

        Assert.Equal(graphClass.Id, summary.Id);
        Assert.Equal("OrderService", summary.Name);
        Assert.Equal("Fixture.Services", summary.Namespace);
        Assert.Equal(NodeKind.Service, summary.Kind);
        Assert.Equal("Fixture/Services/OrderService.cs", summary.File);
        Assert.Equal(5, summary.StartLine);
        Assert.Equal(40, summary.EndLine);
        Assert.Equal(2, summary.MethodCount);
    }

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

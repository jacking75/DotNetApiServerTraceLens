using System.Text.Json;
using TraceLens.Analyzer.Models;
using TraceLens.Mcp;
using Xunit;

namespace TraceLens.Mcp.Tests;

public sealed class TraceLensToolsTests
{
    [Fact]
    public async Task Tools_ReadGraphSourceAndRelationships()
    {
        await using var fixture = await ToolFixture.CreateAsync();
        var tools = new TraceLensTools(new GraphFileStore(fixture.GraphPath));

        var node = await tools.GetNodeAsync("Fixture.Controller.Entry()", CancellationToken.None);
        var source = await tools.GetSourceAsync("Controller.cs", 1, 2, CancellationToken.None);
        var callers = await tools.GetCallersAsync("Fixture.Service.Run()", CancellationToken.None);
        var callees = await tools.GetCalleesAsync("Fixture.Controller.Entry()", CancellationToken.None);
        var entryPoints = await tools.GetEntryPointsAsync(CancellationToken.None);
        var search = await tools.SearchSymbolAsync("service", CancellationToken.None);

        Assert.Equal("Controller.Entry", node.Node.DisplayName);
        Assert.Contains("Entry", node.Source.Content, StringComparison.Ordinal);
        Assert.Equal(2, source.EndLine);
        Assert.Single(callers);
        Assert.Single(callees);
        Assert.Single(entryPoints);
        Assert.Equal("Fixture.Service.Run()", Assert.Single(search).Id);
    }

    private sealed class ToolFixture(string directory, string graphPath) : IAsyncDisposable
    {
        public string GraphPath { get; } = graphPath;

        public static async Task<ToolFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "tracelens-mcp-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "Controller.cs");
            await File.WriteAllTextAsync(sourcePath, "void Entry()\n{\n}\n");

            var controller = new GraphNode(
                "Fixture.Controller.Entry()", NodeKind.Controller, "Controller.Entry", "Fixture", "Fixture.Controller", "Controller.cs", 1, 3);
            var service = new GraphNode(
                "Fixture.Service.Run()", NodeKind.Service, "Service.Run", "Fixture", "Fixture.Service", "Service.cs", 1, 1);
            var graph = new GraphDocument(
                1,
                Path.Combine(directory, "Fixture.sln"),
                DateTimeOffset.Parse("2026-08-11T00:00:00+09:00"),
                [new EntryPoint(controller.Id, "GET", "/fixture")],
                [],
                [controller, service],
                [new GraphEdge(controller.Id, service.Id)],
                new Dictionary<string, string>());
            var graphDirectory = Path.Combine(directory, ".tracelens");
            Directory.CreateDirectory(graphDirectory);
            var graphPath = Path.Combine(graphDirectory, "graph.json");
            await File.WriteAllTextAsync(graphPath, JsonSerializer.Serialize(graph, GraphDocument.JsonOptions));
            return new ToolFixture(directory, graphPath);
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(directory, true);
            return ValueTask.CompletedTask;
        }
    }
}

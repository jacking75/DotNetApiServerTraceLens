using System.Text.Json;
using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Server.Tests;

public sealed class McpConfigWriterTests
{
    [Fact]
    public async Task WriteAsync_CreatesConfigForCurrentGraph()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-config-tests", Guid.NewGuid().ToString("N"));
        var traceLensDirectory = Path.Combine(directory, ".tracelens");
        Directory.CreateDirectory(traceLensDirectory);
        var graphPath = Path.Combine(traceLensDirectory, "graph.json");
        await File.WriteAllTextAsync(graphPath, "{}");
        var graph = new GraphDocument(
            1,
            Path.Combine(directory, "Fixture.sln"),
            DateTimeOffset.Parse("2026-08-11T00:00:00+09:00"),
            [],
            [],
            [],
            new Dictionary<string, string>());

        try
        {
            var configPath = await new McpConfigWriter().WriteAsync(graph);
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
            var server = config.RootElement.GetProperty("mcpServers").GetProperty("tracelens");
            var arguments = server.GetProperty("args").EnumerateArray().Select(item => item.GetString()).ToArray();

            Assert.Equal("stdio", server.GetProperty("type").GetString());
            Assert.Equal("dotnet", server.GetProperty("command").GetString());
            Assert.Contains(graphPath, arguments);
            Assert.EndsWith("TraceLens.Mcp.dll", arguments[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

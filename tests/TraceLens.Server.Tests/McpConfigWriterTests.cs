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

    /// <summary>
    /// 사용자가 같은 파일에 적어 둔 다른 MCP 서버를 덮어쓰면 안 된다.
    /// 예전에는 File.Create로 파일 전체를 갈아 끼웠다.
    /// </summary>
    [Fact]
    public async Task WriteAsync_KeepsOtherServersAndKeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-config-tests", Guid.NewGuid().ToString("N"));
        var traceLensDirectory = Path.Combine(directory, ".tracelens");
        Directory.CreateDirectory(traceLensDirectory);
        await File.WriteAllTextAsync(Path.Combine(traceLensDirectory, "graph.json"), "{}");
        var configPath = Path.Combine(traceLensDirectory, "mcp.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "mcpServers": {
                "other": { "type": "stdio", "command": "node", "args": ["other.js"] },
                "tracelens": { "type": "stdio", "command": "old", "args": [] }
              },
              "userSetting": "keep-me"
            }
            """);
        var graph = CreateGraph(directory);

        try
        {
            await new McpConfigWriter().WriteAsync(graph);
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
            var servers = config.RootElement.GetProperty("mcpServers");

            Assert.Equal("node", servers.GetProperty("other").GetProperty("command").GetString());
            Assert.Equal("dotnet", servers.GetProperty("tracelens").GetProperty("command").GetString());
            Assert.Equal("keep-me", config.RootElement.GetProperty("userSetting").GetString());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>읽지 못하는 파일은 .bak으로 남기고 새로 쓴다. 사용자가 잃는 것은 이미 깨진 파일뿐이다.</summary>
    [Fact]
    public async Task WriteAsync_BacksUpBrokenConfig()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-config-tests", Guid.NewGuid().ToString("N"));
        var traceLensDirectory = Path.Combine(directory, ".tracelens");
        Directory.CreateDirectory(traceLensDirectory);
        await File.WriteAllTextAsync(Path.Combine(traceLensDirectory, "graph.json"), "{}");
        var configPath = Path.Combine(traceLensDirectory, "mcp.json");
        await File.WriteAllTextAsync(configPath, "이건 JSON이 아니다");

        try
        {
            await new McpConfigWriter().WriteAsync(CreateGraph(directory));

            Assert.True(File.Exists(configPath + ".bak"));
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
            Assert.True(config.RootElement.GetProperty("mcpServers").TryGetProperty("tracelens", out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static GraphDocument CreateGraph(string directory) => new(
        1,
        Path.Combine(directory, "Fixture.sln"),
        DateTimeOffset.Parse("2026-08-11T00:00:00+09:00"),
        [],
        [],
        [],
        [],
        new Dictionary<string, string>());
}

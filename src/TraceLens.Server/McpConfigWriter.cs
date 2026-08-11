using System.Text.Json;
using TraceLens.Analyzer.Models;

namespace TraceLens.Server;

public interface IMcpConfigWriter
{
    Task<string> WriteAsync(GraphDocument graph, CancellationToken cancellationToken = default);
}

public sealed class McpConfigWriter : IMcpConfigWriter
{
    public async Task<string> WriteAsync(
        GraphDocument graph,
        CancellationToken cancellationToken = default)
    {
        var mcpAssemblyPath = Path.Combine(AppContext.BaseDirectory, "TraceLens.Mcp.dll");
        if (!File.Exists(mcpAssemblyPath))
        {
            throw new FileNotFoundException("TraceLens MCP 서버 실행 파일을 찾을 수 없다.", mcpAssemblyPath);
        }

        var solutionDirectory = Path.GetDirectoryName(graph.SolutionPath)
            ?? throw new InvalidDataException("그래프의 solutionPath가 올바르지 않다.");
        var traceLensDirectory = Path.Combine(solutionDirectory, ".tracelens");
        Directory.CreateDirectory(traceLensDirectory);
        var graphPath = Path.Combine(traceLensDirectory, "graph.json");
        if (!File.Exists(graphPath))
        {
            throw new FileNotFoundException("분석 결과 graph.json을 찾을 수 없다.", graphPath);
        }

        var configPath = Path.Combine(traceLensDirectory, "mcp.json");
        var config = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["tracelens"] = new
                {
                    type = "stdio",
                    command = "dotnet",
                    args = new[] { mcpAssemblyPath, "--graph", graphPath }
                }
            }
        };
        await using var stream = File.Create(configPath);
        await JsonSerializer.SerializeAsync(
            stream,
            config,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true },
            cancellationToken);
        return configPath;
    }
}

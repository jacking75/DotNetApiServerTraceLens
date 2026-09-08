using System.Runtime.InteropServices;
using System.Text.Json;
using TraceLens.Analyzer.Models;

namespace TraceLens.Server;

/// <param name="Format">json 또는 toml. 편집기마다 설정 형식이 다르다.</param>
/// <param name="Note">기존 설정을 덮어쓰지 않고 어떻게 합쳐야 하는지 설명한다.</param>
public sealed record McpHostSnippet(string Host, string File, string Format, string Content, string Note);

public sealed record McpSetupPreview(
    string GraphPath,
    string Command,
    IReadOnlyList<string> Args,
    IReadOnlyList<McpHostSnippet> Hosts,
    string? Warning);

public interface IMcpConfigWriter
{
    /// <summary>인앱 실행에 쓸 <c>.tracelens/mcp.json</c>을 만들고 그 경로를 돌려준다.</summary>
    Task<string> WriteAsync(GraphDocument graph, CancellationToken cancellationToken = default);

    /// <summary>사용자가 자기 에디터 설정에 직접 붙일 수 있는 미리보기. 기존 설정을 덮어쓰지 않는다.</summary>
    McpSetupPreview BuildPreview(GraphDocument graph, string? offlineGraphPath);
}

public sealed class McpConfigWriter : IMcpConfigWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// MCP 호스트 실행 파일 후보다.
    /// 배포 패키지(.NET tool·portable ZIP)에서는 <c>tracelens</c> 실행 파일이 있고,
    /// 소스에서 실행할 때는 <c>TraceLens.Mcp.dll</c>을 <c>dotnet</c>으로 띄운다.
    /// </summary>
    public static IReadOnlyList<string> GetHostCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "tracelens.exe" : "tracelens";
        return
        [
            Path.Combine(baseDirectory, executableName),
            Path.Combine(baseDirectory, "TraceLens.Mcp.dll")
        ];
    }

    public async Task<string> WriteAsync(GraphDocument graph, CancellationToken cancellationToken = default)
    {
        var solutionDirectory = graph.RootDirectory;
        if (string.IsNullOrEmpty(solutionDirectory))
        {
            throw new InvalidDataException("그래프의 solutionPath가 올바르지 않다.");
        }

        var traceLensDirectory = Path.Combine(solutionDirectory, ".tracelens");
        Directory.CreateDirectory(traceLensDirectory);
        var graphPath = Path.Combine(traceLensDirectory, "graph.json");
        if (!File.Exists(graphPath))
        {
            throw new FileNotFoundException("분석 결과 graph.json을 찾을 수 없다.", graphPath);
        }

        var launch = ResolveLaunch(graphPath);
        var configPath = Path.Combine(traceLensDirectory, "mcp.json");
        var config = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["tracelens"] = new
                {
                    type = "stdio",
                    command = launch.Command,
                    args = launch.Args
                }
            }
        };
        await using var stream = File.Create(configPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
        return configPath;
    }

    public McpSetupPreview BuildPreview(GraphDocument graph, string? offlineGraphPath)
    {
        var graphPath = offlineGraphPath ?? Path.Combine(graph.RootDirectory, ".tracelens", "graph.json");
        var launch = ResolveLaunch(graphPath);
        var argsJson = JsonSerializer.Serialize(launch.Args);
        var argsToml = "[" + string.Join(", ", launch.Args.Select(item => "\"" + item.Replace("\\", "\\\\") + "\"")) + "]";

        var claudeCode = JsonSerializer.Serialize(
            new
            {
                mcpServers = new Dictionary<string, object>
                {
                    ["tracelens"] = new { type = "stdio", command = launch.Command, args = launch.Args }
                }
            },
            JsonOptions);
        var vsCode = JsonSerializer.Serialize(
            new
            {
                servers = new Dictionary<string, object>
                {
                    ["tracelens"] = new { type = "stdio", command = launch.Command, args = launch.Args }
                }
            },
            JsonOptions);
        var codex = $"""
            [mcp_servers.tracelens]
            command = "{launch.Command.Replace("\\", "\\\\")}"
            args = {argsToml}
            """;

        return new McpSetupPreview(
            graphPath,
            launch.Command,
            launch.Args,
            [
                new McpHostSnippet(
                    "Claude Code",
                    Path.Combine(graph.RootDirectory, ".mcp.json"),
                    "json",
                    claudeCode,
                    "이미 .mcp.json이 있으면 mcpServers 아래에 tracelens 항목만 더한다. 파일 전체를 덮어쓰지 않는다."),
                new McpHostSnippet(
                    "Codex CLI",
                    "~/.codex/config.toml",
                    "toml",
                    codex,
                    "기존 config.toml 끝에 이 블록을 더한다."),
                new McpHostSnippet(
                    "VS Code",
                    Path.Combine(graph.RootDirectory, ".vscode", "mcp.json"),
                    "json",
                    vsCode,
                    "이미 파일이 있으면 servers 아래에 tracelens 항목만 더한다.")
            ],
            File.Exists(graphPath) ? null : $"아직 그래프 파일이 없다: {graphPath}. 먼저 분석해야 한다.");
    }

    /// <summary>배포 레이아웃에 맞게 실행 명령을 고른다.</summary>
    private static (string Command, string[] Args) ResolveLaunch(string graphPath)
    {
        var candidates = GetHostCandidates();
        var executable = candidates[0];
        if (File.Exists(executable))
        {
            return (executable, ["mcp", "--graph", graphPath]);
        }

        var assembly = candidates[1];
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException("TraceLens MCP 서버 실행 파일을 찾을 수 없다.", assembly);
        }

        return ("dotnet", [assembly, "--graph", graphPath]);
    }
}

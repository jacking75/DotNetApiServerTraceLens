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
        var entry = new Dictionary<string, object?>
        {
            ["type"] = "stdio",
            ["command"] = launch.Command,
            ["args"] = launch.Args
        };

        // 기존 파일이 있으면 tracelens 항목만 갈아 끼우고 나머지 키(다른 MCP 서버·사용자 설정)는 그대로 둔다.
        var document = await ReadExistingAsync(configPath, cancellationToken);
        var servers = document.TryGetValue("mcpServers", out var existingServers) &&
            existingServers is Dictionary<string, object?> map
                ? map
                : new Dictionary<string, object?>(StringComparer.Ordinal);
        servers["tracelens"] = entry;
        document["mcpServers"] = servers;

        var json = JsonSerializer.Serialize(document, JsonOptions);
        await File.WriteAllTextAsync(configPath, json, cancellationToken);
        return configPath;
    }

    /// <summary>
    /// 기존 <c>mcp.json</c>을 읽어 사전으로 돌려준다.
    /// 파일이 없으면 빈 사전이고, 깨져서 읽지 못하면 <c>.bak</c>으로 옮긴 뒤 빈 사전으로 시작한다.
    /// </summary>
    private static async Task<Dictionary<string, object?>> ReadExistingAsync(
        string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        try
        {
            var text = await File.ReadAllTextAsync(configPath, cancellationToken);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(text, JsonOptions);
            return Normalize(parsed) ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            TryBackup(configPath);
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }
    }

    /// <summary><c>JsonElement</c>로 읽힌 값을 다시 직렬화할 수 있는 형태로 바꾼다.</summary>
    private static Dictionary<string, object?>? Normalize(Dictionary<string, object?>? source)
    {
        if (source is null)
        {
            return null;
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            result[key] = value is JsonElement { ValueKind: JsonValueKind.Object } element
                ? Normalize(element.Deserialize<Dictionary<string, object?>>(JsonOptions))
                : value;
        }

        return result;
    }

    private static void TryBackup(string configPath)
    {
        try
        {
            File.Copy(configPath, configPath + ".bak", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 백업을 못 만들어도 새 설정은 쓴다. 사용자가 잃는 것은 이미 읽지 못하는 파일뿐이다.
        }
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

using System.Runtime.InteropServices;
using System.Text.Json;
using TraceLens.Analyzer.Models;

namespace TraceLens.Server;

/// <param name="Format">json 또는 toml. 편집기마다 설정 형식이 다르다.</param>
/// <param name="Note">기존 설정을 덮어쓰지 않고 어떻게 합쳐야 하는지 설명한다.</param>
public sealed record McpHostSnippet(string Host, string File, string Format, string Content, string Note);

/// <param name="Command">복사해서 그대로 실행할 수 있는 한 줄. 경로는 항상 따옴표로 감싼다.</param>
public sealed record McpOneLiner(string Host, string Command);

public sealed record McpSetupPreview(
    string GraphPath,
    string Command,
    IReadOnlyList<string> Args,
    IReadOnlyList<McpHostSnippet> Hosts,
    string? Warning)
{
    /// <summary>호스트별 등록 원라이너. 붙여넣기 한 번으로 끝나게 한다.</summary>
    public IReadOnlyList<McpOneLiner> OneLiners { get; init; } = [];

    /// <summary>연결을 확인하는 질문.</summary>
    public string VerifyPrompt { get; init; } = string.Empty;

    /// <summary>대상 저장소의 CLAUDE.md·AGENTS.md에 붙이는 안내문.</summary>
    public string AgentGuide { get; init; } = string.Empty;
}

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
    /// 대상 저장소의 CLAUDE.md·AGENTS.md에 붙이는 안내문이다.
    /// 이게 없으면 에이전트는 평소처럼 grep으로 헤맨다. 문서와 대화상자가 같은 내용을 쓴다.
    /// </summary>
    public const string AgentGuide = """
        ## TraceLens MCP 사용 규칙

        이 저장소의 호출 관계·DB 접근·미들웨어를 물을 때는 grep보다 tracelens MCP를 먼저 쓴다.

        1. 무엇을 묻든 `get_analysis_status`를 먼저 부른다. 신선도가 `stale`이면 답에 그 사실을 적는다.
        2. "이 API가 뭘 하나" → `get_entrypoints` → `get_entrypoint_flow` → `get_context(intent=explain)`.
        3. "고치면 뭐가 깨지나" → `search_symbol`로 nodeId를 찾고 → `get_impact`.
        4. "쓰기 경로가 안전한가" → `list_write_paths` → `get_findings(ruleId=COMP-)` → `get_context(intent=investigateData)`.
        5. "이 요청이 어떤 미들웨어를 지나나" → `get_middleware_pipeline`.
        6. "이 서비스가 어떻게 등록됐나" → `get_startup_flow`.
        7. "이 클래스에 뭐가 있나" → `list_classes` → `get_class`(멤버의 노드 ID가 함께 온다).
        8. "수정 전후 비교" → `list_snapshots`로 기준을 고르고 → `compare_snapshots`.
        9. 호출자 목록이 비어 있다고 "호출 없음"으로 읽지 않는다. `boundaries`(확정하지 못한 호출)를 함께 본다.
        10. 분석 결과가 오래됐어도 도구가 재분석하지 않는다. 사용자에게 `tracelens analyze <대상>` 실행을 요청한다.
        11. 도구가 준 파일·줄을 근거로 인용하고, 확정하지 못한 것은 추정이라고 적는다.
        12. 실패는 `{ "isError": true, "error": { "code" } }`로 온다. `code`로 분기하고 메시지를 지어내지 않는다.
        """;

    /// <summary>연결이 실제로 됐는지 확인하는 질문. 파일·줄을 인용하면 연결된 것이다.</summary>
    public const string VerifyPrompt =
        "tracelens MCP로 이 저장소의 HTTP 진입점 목록을 가져오고, 그중 하나의 호출 흐름을 파일·줄과 함께 보여 줘.";


    /// <summary>
    /// MCP 호스트 실행 파일 후보다.
    /// 배포 패키지(.NET tool·portable ZIP)에서는 <c>tracelens</c> 실행 파일이 있고,
    /// 소스에서 실행할 때는 <c>TraceLens.Mcp.dll</c>을 <c>dotnet</c>으로 띄운다.
    /// </summary>
    public static IReadOnlyList<string> GetHostCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "tracelens.exe" : "tracelens";
        var desktopExecutable = Environment.ProcessPath;
        var desktopName = Path.GetFileNameWithoutExtension(desktopExecutable);
        var executable = desktopExecutable is not null && desktopName is not null &&
            desktopName.StartsWith("TraceLens.Desktop", StringComparison.OrdinalIgnoreCase)
            ? desktopExecutable
            : Path.Combine(baseDirectory, executableName);
        return
        [
            executable,
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
                    "이미 파일이 있으면 servers 아래에 tracelens 항목만 더한다."),
                new McpHostSnippet(
                    "Cursor",
                    Path.Combine(graph.RootDirectory, ".cursor", "mcp.json"),
                    "json",
                    claudeCode,
                    "Claude Code와 같은 형식이다. 이미 파일이 있으면 mcpServers 아래에 tracelens 항목만 더한다."),
                new McpHostSnippet(
                    "Claude Desktop",
                    ClaudeDesktopConfigPath(),
                    "json",
                    claudeCode,
                    "Claude Code와 같은 형식이다. 설정을 고친 뒤 Claude Desktop을 다시 시작해야 반영된다."),
                new McpHostSnippet(
                    "Windsurf",
                    Path.Combine(HomeDirectory(), ".codeium", "windsurf", "mcp_config.json"),
                    "json",
                    claudeCode,
                    "Claude Code와 같은 형식이다. mcpServers 아래에 tracelens 항목만 더한다."),
                new McpHostSnippet(
                    "Gemini CLI",
                    Path.Combine(HomeDirectory(), ".gemini", "settings.json"),
                    "json",
                    claudeCode,
                    "settings.json의 mcpServers 아래에 tracelens 항목만 더한다. 파일 전체를 덮어쓰지 않는다.")
            ],
            File.Exists(graphPath) ? null : $"아직 그래프 파일이 없다: {graphPath}. 먼저 분석해야 한다.")
        {
            OneLiners = BuildOneLiners(launch),
            VerifyPrompt = VerifyPrompt,
            AgentGuide = AgentGuide
        };
    }

    /// <summary>
    /// 호스트별 등록 원라이너. 실행 파일 경로와 그래프 경로에 공백이 있어도 깨지지 않게 항상 따옴표로 감싼다.
    /// (`C:\Program Files\...`가 대표 사례다.)
    /// </summary>
    private static IReadOnlyList<McpOneLiner> BuildOneLiners((string Command, string[] Args) launch)
    {
        var quoted = string.Join(' ', new[] { launch.Command }.Concat(launch.Args).Select(Quote));
        return
        [
            new McpOneLiner("Claude Code", $"claude mcp add --scope local tracelens -- {quoted}"),
            new McpOneLiner("Codex CLI", $"codex mcp add tracelens -- {quoted}"),
            new McpOneLiner("VS Code", $"code --add-mcp {Quote($"{{\"name\":\"tracelens\",\"command\":{Quote(launch.Command)},\"args\":[{string.Join(", ", launch.Args.Select(Quote))}]}}")}"),
            new McpOneLiner("Cursor", $"cursor --add-mcp {Quote($"{{\"name\":\"tracelens\",\"command\":{Quote(launch.Command)},\"args\":[{string.Join(", ", launch.Args.Select(Quote))}]}}")}")
        ];
    }

    /// <summary>사용자 홈. 호스트 설정 파일 위치를 만들 때 쓴다.</summary>
    private static string HomeDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Claude Desktop의 설정 파일 위치. 운영체제마다 다르다.</summary>
    private static string ClaudeDesktopConfigPath() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude",
            "claude_desktop_config.json")
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Path.Combine(HomeDirectory(), "Library", "Application Support", "Claude", "claude_desktop_config.json")
            : Path.Combine(HomeDirectory(), ".config", "Claude", "claude_desktop_config.json");

    /// <summary>공백이 없어도 항상 감싼다. "이 예시는 공백에서 깨진다"를 사용자가 판단하게 두지 않는다.</summary>
    private static string Quote(string value) => $"\"{value}\"";

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

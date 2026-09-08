using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using TraceLens.Analyzer;

namespace TraceLens.Context;

public enum DiagnosticSeverity
{
    Ok,
    Warning,
    Error
}

/// <param name="Code">UI 번역과 MCP가 같은 의미로 쓰는 코드.</param>
/// <param name="Message">사람이 읽는 한국어 문장. CLI가 그대로 출력한다.</param>
/// <param name="SuggestedAction">사용자가 다음에 할 일. doctor가 대신 실행하지는 않는다.</param>
/// <param name="Detail">경로·포트처럼 번역하지 않는 값. UI는 코드로 문장을 번역하고 이 값을 덧붙인다.</param>
public sealed record DiagnosticItem(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string SuggestedAction)
{
    public string Detail { get; init; } = string.Empty;
}

public sealed record DiagnosticsReport(
    bool CanAnalyze,
    string ToolVersion,
    string RuntimeVersion,
    string OperatingSystem,
    string? SdkPath,
    IReadOnlyList<string> SupportedTargets,
    IReadOnlyDictionary<string, string> UnsupportedTargets,
    IReadOnlyList<DiagnosticItem> Items);

public sealed record DiagnosticsOptions
{
    /// <summary>분석하려는 .sln/.csproj. 비우면 대상 관련 검사를 건너뛴다.</summary>
    public string? TargetPath { get; init; }

    /// <summary>UI 서버가 쓸 포트. 0이면 검사하지 않는다.</summary>
    public int Port { get; init; }

    /// <summary>MCP 호스트 실행 파일 후보 경로.</summary>
    public IReadOnlyList<string> McpHostCandidates { get; init; } = [];

    /// <summary>있으면 좋은 외부 CLI(에디터·AI). 없어도 분석 실패로 보지 않는다.</summary>
    public IReadOnlyList<string> OptionalCommands { get; init; } = ["code", "claude", "codex"];
}

/// <summary>
/// 실행 환경을 점검해서 실패 원인과 다음 행동을 알려준다.
/// API 키나 전체 환경 변수는 수집하지 않고, restore·설치·설정 변경을 자동으로 실행하지 않는다.
/// 서버 없이도 실행할 수 있어야 하므로 순수 함수로 둔다.
/// </summary>
public sealed class EnvironmentDiagnostics
{
    public DiagnosticsReport Run(DiagnosticsOptions options)
    {
        var items = new List<DiagnosticItem>();
        string? sdkPath = null;

        try
        {
            sdkPath = SolutionLoader.FindLatestSdkDirectory();
            items.Add(new DiagnosticItem(
                "SDK_FOUND",
                DiagnosticSeverity.Ok,
                $".NET SDK를 찾았다: {sdkPath}",
                string.Empty) { Detail = sdkPath });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            items.Add(new DiagnosticItem(
                "SDK_NOT_FOUND",
                DiagnosticSeverity.Error,
                $"분석에 쓸 .NET SDK를 찾지 못했다: {exception.Message}",
                ".NET 8 이상 SDK를 설치하고 `dotnet --info`로 확인해야 한다.") { Detail = exception.Message });
        }

        CheckTarget(options, items);
        CheckGlobalJson(options, items);
        CheckPort(options, items);
        CheckMcpHost(options, items);
        CheckOptionalCommands(options, items);

        return new DiagnosticsReport(
            items.All(item => item.Severity != DiagnosticSeverity.Error),
            TraceLensAnalyzer.AnalyzerVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            sdkPath,
            SolutionLoader.SupportedExtensions,
            SolutionLoader.UnsupportedExtensions,
            items);
    }

    private static void CheckTarget(DiagnosticsOptions options, List<DiagnosticItem> items)
    {
        if (string.IsNullOrWhiteSpace(options.TargetPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(options.TargetPath);
        var extension = Path.GetExtension(fullPath);
        if (SolutionLoader.UnsupportedExtensions.TryGetValue(extension, out var reason))
        {
            items.Add(new DiagnosticItem(
                "UNSUPPORTED_TARGET_FORMAT",
                DiagnosticSeverity.Error,
                $"{extension} 형식은 아직 지원하지 않는다. {reason}",
                $"지원 형식: {string.Join(", ", SolutionLoader.SupportedExtensions)}") { Detail = fullPath });
            return;
        }

        if (!SolutionLoader.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new DiagnosticItem(
                "UNSUPPORTED_TARGET_FORMAT",
                DiagnosticSeverity.Error,
                $"분석 대상은 {string.Join(" 또는 ", SolutionLoader.SupportedExtensions)}여야 한다: {fullPath}",
                "분석할 솔루션이나 프로젝트 파일을 골라야 한다.") { Detail = fullPath });
            return;
        }

        if (!File.Exists(fullPath))
        {
            items.Add(new DiagnosticItem(
                "TARGET_NOT_FOUND",
                DiagnosticSeverity.Error,
                $"분석 대상을 찾을 수 없다: {fullPath}",
                "경로를 확인하거나 `tracelens doctor --target <경로>`로 다시 확인해야 한다.") { Detail = fullPath });
            return;
        }

        items.Add(new DiagnosticItem("TARGET_FOUND", DiagnosticSeverity.Ok, $"분석 대상: {fullPath}", string.Empty)
        {
            Detail = fullPath
        });

        var outputDirectory = Path.Combine(Path.GetDirectoryName(fullPath)!, ".tracelens");
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var probe = Path.Combine(outputDirectory, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            items.Add(new DiagnosticItem(
                "OUTPUT_WRITABLE", DiagnosticSeverity.Ok, $"분석 결과를 저장할 수 있다: {outputDirectory}", string.Empty)
            {
                Detail = outputDirectory
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            items.Add(new DiagnosticItem(
                "OUTPUT_NOT_WRITABLE",
                DiagnosticSeverity.Error,
                $"분석 결과 폴더에 쓸 수 없다: {outputDirectory} ({exception.Message})",
                "폴더 권한을 확인하거나 쓰기 가능한 위치로 프로젝트를 옮겨야 한다.") { Detail = outputDirectory });
        }
    }

    private static void CheckGlobalJson(DiagnosticsOptions options, List<DiagnosticItem> items)
    {
        if (string.IsNullOrWhiteSpace(options.TargetPath))
        {
            return;
        }

        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(options.TargetPath))!);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "global.json");
            if (File.Exists(path))
            {
                items.Add(new DiagnosticItem(
                    "SDK_MISMATCH",
                    DiagnosticSeverity.Warning,
                    $"global.json이 있다: {path}. 고정한 SDK 버전이 설치돼 있어야 분석이 된다.",
                    "`dotnet --list-sdks`로 global.json이 요구하는 버전이 있는지 확인해야 한다.") { Detail = path });
                return;
            }

            directory = directory.Parent;
        }
    }

    private static void CheckPort(DiagnosticsOptions options, List<DiagnosticItem> items)
    {
        if (options.Port <= 0)
        {
            return;
        }

        try
        {
            var inUse = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == options.Port);
            items.Add(inUse
                ? new DiagnosticItem(
                    "PORT_IN_USE",
                    DiagnosticSeverity.Warning,
                    $"포트 {options.Port}을(를) 이미 쓰고 있다.",
                    "이미 실행 중인 TraceLens가 있는지 확인하거나 그 프로세스를 종료해야 한다.") { Detail = options.Port.ToString() }
                : new DiagnosticItem("PORT_FREE", DiagnosticSeverity.Ok, $"포트 {options.Port}을(를) 쓸 수 있다.", string.Empty)
                {
                    Detail = options.Port.ToString()
                });
        }
        catch (NetworkInformationException exception)
        {
            items.Add(new DiagnosticItem(
                "PORT_UNKNOWN",
                DiagnosticSeverity.Warning,
                $"포트 사용 여부를 확인하지 못했다: {exception.Message}",
                "서버를 실행해 보고 실패하면 포트 충돌을 의심해야 한다.") { Detail = exception.Message });
        }
    }

    private static void CheckMcpHost(DiagnosticsOptions options, List<DiagnosticItem> items)
    {
        if (options.McpHostCandidates.Count == 0)
        {
            return;
        }

        var found = options.McpHostCandidates.FirstOrDefault(File.Exists);
        items.Add(found is not null
            ? new DiagnosticItem("MCP_HOST_FOUND", DiagnosticSeverity.Ok, $"MCP 호스트를 찾았다: {found}", string.Empty)
            {
                Detail = found
            }
            : new DiagnosticItem(
                "MCP_HOST_MISSING",
                DiagnosticSeverity.Warning,
                "MCP 호스트 실행 파일을 찾지 못했다. AI 에이전트 연동만 영향을 받고 분석은 그대로 동작한다.",
                $"확인한 경로: {string.Join(", ", options.McpHostCandidates)}") { Detail = string.Join(", ", options.McpHostCandidates) });
    }

    private static void CheckOptionalCommands(DiagnosticsOptions options, List<DiagnosticItem> items)
    {
        foreach (var command in options.OptionalCommands)
        {
            var found = FindOnPath(command);
            items.Add(found is not null
                ? new DiagnosticItem($"CLI_{command.ToUpperInvariant()}_FOUND", DiagnosticSeverity.Ok, $"{command} 명령을 찾았다: {found}", string.Empty)
                {
                    Detail = found
                }
                : new DiagnosticItem(
                    $"CLI_{command.ToUpperInvariant()}_MISSING",
                    DiagnosticSeverity.Ok,
                    $"{command} 명령이 없다. 선택 기능이라 분석에는 영향이 없다.",
                    $"{command}를 쓰려면 설치하고 PATH에 넣어야 한다.") { Detail = command });
        }
    }

    private static string? FindOnPath(string command)
    {
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim('"'), command + extension.ToLowerInvariant());
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // PATH에 잘못된 문자가 있는 항목은 건너뛴다.
                }
            }
        }

        return null;
    }
}

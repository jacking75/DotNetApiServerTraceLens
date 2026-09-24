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

/// <param name="LogFilePath">이번 실행이 쓰는 로그 파일. 없으면 null이다.</param>
/// <param name="RecentErrors">최근 오류 요약(최대 3건). 비밀값·환경 변수는 담지 않는다.</param>
public sealed record DiagnosticsLogInfo(string? LogFilePath, IReadOnlyList<string> RecentErrors);

public sealed record DiagnosticsReport(
    bool CanAnalyze,
    string ToolVersion,
    string RuntimeVersion,
    string OperatingSystem,
    string? SdkPath,
    IReadOnlyList<string> SupportedTargets,
    IReadOnlyDictionary<string, string> UnsupportedTargets,
    IReadOnlyList<DiagnosticItem> Items)
{
    /// <summary>로그 파일 위치와 최근 오류. 호스트(서버)가 채워 넣고, 없으면 null이다.</summary>
    public DiagnosticsLogInfo? Log { get; init; }
}

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

    /// <summary>
    /// 이 포트를 지금 <b>자기 자신이</b> 듣고 있는지.
    /// 서버 안에서 점검을 부르면 리스너는 서버 자신이라 <c>PORT_IN_USE</c>가 항상 뜬다 —
    /// "이미 실행 중인 TraceLens를 종료하라"는 자기 자신을 문제로 보고하는 진단이다(T48).
    /// CLI `doctor`는 아직 서버를 띄우기 전이라 false로 둔다.
    /// </summary>
    public bool SelfListening { get; init; }
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
                ".NET 10 이상 SDK를 설치하고 `dotnet --info`로 확인해야 한다.") { Detail = exception.Message });
        }

        CheckSdkVersion(sdkPath, items);
        CheckMsBuildRegistration(items);
        CheckOperatingSystem(items);
        CheckTarget(options, items);
        CheckTargetFrameworks(options, items);
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

    /// <summary>
    /// SDK를 찾았더라도 버전이 낮으면 net10.0 프로젝트를 열지 못한다.
    /// 예전에는 SDK 6.0만 있어도 "분석할 수 있다"고 말했다 — doctor가 통과시킨 뒤 실제로 실패하는 대표 사례다.
    /// </summary>
    private static void CheckSdkVersion(string? sdkPath, List<DiagnosticItem> items)
    {
        if (sdkPath is null)
        {
            return;
        }

        var version = SolutionLoader.ReadSdkVersion(Path.GetFileName(sdkPath));
        if (version is null || version.Major >= SolutionLoader.MinimumSdkMajor)
        {
            return;
        }

        var installed = string.Join(", ", SolutionLoader.ListSdkDirectories().Select(Path.GetFileName).Take(5));
        items.Add(new DiagnosticItem(
            "SDK_TOO_OLD",
            DiagnosticSeverity.Error,
            $"가장 최신 .NET SDK가 {version}이라 분석에 쓸 수 없다(최소 {SolutionLoader.MinimumSdkMajor}.0).",
            $".NET {SolutionLoader.MinimumSdkMajor} 이상 SDK를 설치해야 한다.") { Detail = installed });
    }

    /// <summary>
    /// 어느 MSBuild를 등록했는지 알려 준다. 등록 전이면(분석을 아직 안 했으면) 아무것도 말하지 않는다 —
    /// 여기서 등록을 강제하면 doctor가 무거워지고 부작용이 생긴다.
    /// </summary>
    private static void CheckMsBuildRegistration(List<DiagnosticItem> items)
    {
        if (SolutionLoader.RegisteredMsBuildDescription is not { Length: > 0 } description)
        {
            return;
        }

        items.Add(new DiagnosticItem(
            "MSBUILD_REGISTERED",
            DiagnosticSeverity.Ok,
            $"MSBuild를 등록했다: {description}",
            string.Empty) { Detail = description });
    }

    /// <summary>Windows 밖에서는 아직 검증하지 않았다. "된다"고도 "안 된다"고도 말하지 않는다.</summary>
    private static void CheckOperatingSystem(List<DiagnosticItem> items)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        items.Add(new DiagnosticItem(
            "OS_UNVERIFIED",
            DiagnosticSeverity.Warning,
            $"이 운영체제({RuntimeInformation.OSDescription})는 아직 검증하지 않았다.",
            "동작하거나 실패하면 CONTRIBUTING의 신고 형식으로 알려 주면 도움이 된다.")
        {
            Detail = RuntimeInformation.OSDescription
        });
    }

    /// <summary>
    /// 대상 프로젝트의 <c>TargetFramework(s)</c>를 읽어 설치된 SDK 메이저와 대조한다.
    /// 복원이 된 적 없는 프로젝트(<c>obj/project.assets.json</c> 없음)는 NuGet 접근도 함께 본다.
    /// 여기서는 <b>이름 해석만</b> 하고 네트워크 요청은 하지 않는다.
    /// </summary>
    private static void CheckTargetFrameworks(DiagnosticsOptions options, List<DiagnosticItem> items)
    {
        if (string.IsNullOrWhiteSpace(options.TargetPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(options.TargetPath);
        if (!File.Exists(fullPath))
        {
            return;
        }

        var projects = FindProjectFiles(fullPath).Take(MaxInspectedProjects).ToArray();
        if (projects.Length == 0)
        {
            return;
        }

        // SDK N은 netX(X ≤ N)를 빌드할 수 있다. 그래서 "정확히 같은 메이저가 있는가"가 아니라
        // "가장 높은 SDK보다 새 프레임워크를 요구하는가"만 본다. 반대로 보면 net10.0 프로젝트에
        // SDK 10만 깔린 정상 환경이 경고로 뜬다.
        var newestSdkMajor = SolutionLoader.ListSdkDirectories()
            .Select(path => SolutionLoader.ReadSdkVersion(Path.GetFileName(path)))
            .Where(version => version is not null)
            .Select(version => version!.Major)
            .DefaultIfEmpty(0)
            .Max();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var needsRestore = new List<string>();

        foreach (var project in projects)
        {
            foreach (var moniker in ReadTargetFrameworks(project))
            {
                var match = TargetFrameworkPattern.Match(moniker);
                if (match.Success &&
                    int.TryParse(match.Groups["major"].Value, out var major) &&
                    newestSdkMajor > 0 &&
                    major > newestSdkMajor)
                {
                    missing.Add(moniker);
                }
            }

            var objDirectory = Path.Combine(Path.GetDirectoryName(project)!, "obj");
            if (!File.Exists(Path.Combine(objDirectory, "project.assets.json")))
            {
                needsRestore.Add(Path.GetFileName(project));
            }
        }

        if (missing.Count > 0)
        {
            items.Add(new DiagnosticItem(
                "TFM_NOT_INSTALLED",
                DiagnosticSeverity.Warning,
                $"대상이 설치된 SDK보다 새 프레임워크를 요구한다: {string.Join(", ", missing)}(설치된 최신 SDK는 {newestSdkMajor}.x다).",
                "해당 버전의 .NET SDK를 설치하거나 `dotnet --list-sdks`로 확인해야 한다.")
            {
                Detail = string.Join(", ", missing)
            });
        }

        if (needsRestore.Count > 0 && !CanResolveNuGetHost())
        {
            items.Add(new DiagnosticItem(
                "NUGET_UNREACHABLE",
                DiagnosticSeverity.Warning,
                $"복원되지 않은 프로젝트가 {needsRestore.Count}개인데 nuget.org 이름을 해석하지 못했다(폐쇄망일 수 있다).",
                "사내 NuGet 소스를 설정하거나 복원된 폴더에서 분석해야 한다. 이 확인은 이름 해석만 하고 패키지를 받지 않는다.")
            {
                Detail = string.Join(", ", needsRestore.Take(5))
            });
        }
    }

    /// <summary>대상 아래에서 읽어 볼 프로젝트 수 상한. doctor가 큰 솔루션에서 느려지지 않게 한다.</summary>
    private const int MaxInspectedProjects = 40;

    private static readonly System.Text.RegularExpressions.Regex TargetFrameworkPattern = new(
        @"^net(?<major>\d+)\.\d+",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex TargetFrameworkElement = new(
        @"<TargetFrameworks?>(?<value>[^<]+)</TargetFrameworks?>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static IEnumerable<string> FindProjectFiles(string fullTargetPath)
    {
        if (Path.GetExtension(fullTargetPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            yield return fullTargetPath;
            yield break;
        }

        var directory = Path.GetDirectoryName(fullTargetPath);
        if (directory is null)
        {
            yield break;
        }

        IEnumerable<string> found;
        try
        {
            found = Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                               !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var path in found)
        {
            yield return path;
        }
    }

    private static IEnumerable<string> ReadTargetFrameworks(string projectPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(projectPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (System.Text.RegularExpressions.Match match in TargetFrameworkElement.Matches(text))
        {
            foreach (var moniker in match.Groups["value"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                yield return moniker.Trim();
            }
        }
    }

    /// <summary>nuget.org 이름을 해석할 수 있는지. 패키지를 받지 않고 DNS만 본다.</summary>
    private static bool CanResolveNuGetHost()
    {
        try
        {
            return System.Net.Dns.GetHostEntry("api.nuget.org").AddressList.Length > 0;
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or ArgumentException)
        {
            return false;
        }
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

        // 실제 출력 위치는 분석 뒤에야 정해진다(.csproj면 참조 프로젝트까지 담는 공통 상위 폴더).
        // 분석 전에는 후보 두 곳 — 대상 폴더와, 조상 중 .sln이 있는 폴더 — 을 함께 검사하고 검사한 경로를 적는다.
        var checkedDirectories = new List<string>();
        var failures = new List<string>();
        foreach (var candidate in OutputDirectoryCandidates(fullPath))
        {
            var outputDirectory = Path.Combine(candidate, ".tracelens");
            checkedDirectories.Add(outputDirectory);
            try
            {
                Directory.CreateDirectory(outputDirectory);
                var probe = Path.Combine(outputDirectory, ".write-probe");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{outputDirectory} ({exception.Message})");
            }
        }

        var checkedList = string.Join(", ", checkedDirectories);
        if (failures.Count == 0)
        {
            items.Add(new DiagnosticItem(
                "OUTPUT_WRITABLE", DiagnosticSeverity.Ok, $"분석 결과를 저장할 수 있다: {checkedList}", string.Empty)
            {
                Detail = checkedList
            });
        }
        else
        {
            items.Add(new DiagnosticItem(
                "OUTPUT_NOT_WRITABLE",
                DiagnosticSeverity.Error,
                $"분석 결과 폴더에 쓸 수 없다: {string.Join(" / ", failures)}. 검사한 경로: {checkedList}",
                "폴더 권한을 확인하거나 쓰기 가능한 위치로 프로젝트를 옮겨야 한다.") { Detail = checkedList });
        }
    }

    /// <summary>분석 결과가 저장될 수 있는 폴더 후보. 대상 폴더와, 조상 중 <c>.sln</c>/<c>.slnx</c>이 있는 첫 폴더다.</summary>
    private static IEnumerable<string> OutputDirectoryCandidates(string fullTargetPath)
    {
        var targetDirectory = Path.GetDirectoryName(fullTargetPath);
        if (string.IsNullOrEmpty(targetDirectory))
        {
            yield break;
        }

        yield return targetDirectory;

        var current = new DirectoryInfo(targetDirectory).Parent;
        for (var level = 0; current is not null && level < GraphLocator.MaxAncestorLevels; level++, current = current.Parent)
        {
            if (current.EnumerateFiles("*.sln").Any() || current.EnumerateFiles("*.slnx").Any())
            {
                yield return current.FullName;
                yield break;
            }
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
            items.Add((inUse, options.SelfListening) switch
            {
                // 서버 안에서 부른 점검이다. 그 리스너는 이 프로세스 자신이다.
                (true, true) => new DiagnosticItem(
                    "PORT_SELF",
                    DiagnosticSeverity.Ok,
                    $"이 TraceLens가 포트 {options.Port}으로 실행 중이다.",
                    string.Empty) { Detail = options.Port.ToString() },
                (true, false) => new DiagnosticItem(
                    "PORT_IN_USE",
                    DiagnosticSeverity.Warning,
                    $"포트 {options.Port}을(를) 이미 쓰고 있다.",
                    "실행 중인 TraceLens를 끝내거나(Windows: `Get-Process tracelens | Stop-Process`) " +
                    "`--port <다른 번호>`로 띄워야 한다.") { Detail = options.Port.ToString() },
                _ => new DiagnosticItem(
                    "PORT_FREE", DiagnosticSeverity.Ok, $"포트 {options.Port}을(를) 쓸 수 있다.", string.Empty)
                {
                    Detail = options.Port.ToString()
                }
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

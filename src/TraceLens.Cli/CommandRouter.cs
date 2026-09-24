using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;
using TraceLens.Cli.Commands;
using TraceLens.Context;
using TraceLens.Mcp;
using TraceLens.Server;

namespace TraceLens.Cli;

/// <summary>
/// <c>tracelens</c> 명령의 진입점이다.
/// 명령마다 알맞은 호스트(Server 또는 Mcp)를 부르고, 명령 사이에 상태를 공유하는 전역 호스트는 만들지 않는다.
/// </summary>
public static class CommandRouter
{
    public static async Task<int> RunAsync(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return command switch
        {
            null or "help" or "--help" or "-h" => PrintUsage(),
            "version" or "--version" => PrintVersion(),
            "serve" => await ServeAsync(rest),
            "analyze" => await AnalyzeAsync(rest),
            "impact" => await GraphCommands.ImpactAsync(rest, Console.Out, Console.Error),
            "context" => await GraphCommands.ContextAsync(rest, Console.Out, Console.Error),
            "compare" => await GraphCommands.CompareAsync(rest, Console.Out, Console.Error),
            "mcp" => await McpAsync(rest),
            "doctor" => await Doctor(rest),
            "demo" => await DemoAsync(rest),
            "init" => await InitAsync(rest),
            "config" => await ConfigAsync(rest),
            _ => Unknown(command)
        };
    }

    private static int PrintUsage()
    {
        Console.WriteLine($"""
            TraceLens {TraceLensAnalyzer.AnalyzerVersion} — ASP.NET Core Web API의 호출 경로를 정적으로 살펴보는 로컬 도구다.

            사용법:
              tracelens serve [--target <경로>] [--port <번호>] [--no-browser]
                  UI 서버를 띄운다(기본 http://127.0.0.1:{ServerHost.DefaultPort}, 루프백 전용).
              tracelens analyze <solution-or-project-path> [--json] [--profile] [--output <폴더>] [--config <파일>]
                  한 번 분석하고 결과 요약을 출력한다.
                  결과는 대상과 참조 프로젝트의 공통 상위 폴더의 .tracelens/graph.json에 저장된다(출력 위치는 analyze 결과의 outputPath).
                  --output을 주면 그 폴더의 .tracelens/graph.json에 쓴다. --config로 설정 파일을 따로 지정한다.
                  --profile을 주면 단계별 시간과 최대 메모리를 함께 출력한다.
              tracelens impact <대상> --node <id|"Class.Method"> [--changed-files a.cs,b.cs] [--json]
                  저장된 분석 결과에서 영향 후보 진입점을 찾는다(재분석하지 않는다).
                  "호출자가 없다 → 삭제해도 안전"이 아니다 — 후보 목록이다.
              tracelens context <대상> --node <id> [--kind node|endpoint|class|…] [--intent …] [--budget …] [--json]
                  AI에 넘길 문맥을 표준 출력으로 낸다. 기본은 Markdown이다.
              tracelens compare <대상> --baseline <snapshotId|latest> [--list] [--json]
                  보관한 기준과 현재 분석을 비교한다. 전제 조건이 다르면 종료 코드 3이다.
              tracelens mcp --graph <graph-path>
                  로컬 stdio MCP 서버를 띄운다. AI 에이전트가 이 분석 결과를 읽는다.
              tracelens doctor [--target <경로>] [--json]
                  실행 환경을 점검하고 실패 원인과 다음 행동을 알려준다. 설치·설정 변경은 하지 않는다.
              tracelens demo [--dir <복사할 위치>]
                  예제 프로젝트를 원하는 폴더에 복사하고 분석 방법을 안내한다.
              tracelens init [--target <경로>] [--force]
                  대상 폴더에 tracelens.config.json을 만든다. 이미 있으면 덮어쓰지 않고 차이만 알려준다.
              tracelens config --show [--target <경로>]
                  기본값과 설정 파일을 합친 유효 설정 전체를 출력한다("내 설정이 먹었는지" 확인용).

            종료 코드:
              0 성공 · 1 실패 · 2 인자 오류 · 3 비교 전제 조건 불일치 · 4 분석 결과가 현재 소스와 다름(stale)

            지원하는 분석 대상: {string.Join(", ", SolutionLoader.SupportedExtensions)}
            """);
        return 0;
    }

    private static int PrintVersion()
    {
        Console.WriteLine(TraceLensAnalyzer.AnalyzerVersion);
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"모르는 명령이다: {command}");
        PrintUsage();
        return 2;
    }

    private static async Task<int> ServeAsync(string[] args)
    {
        var port = TryGetInt(args, "--port") ?? ServerHost.DefaultPort;
        if (port is < 1 or > 65535)
        {
            Console.Error.WriteLine($"--port는 1~65535 사이여야 한다: {port}");
            return 2;
        }

        var options = new ServerOptions
        {
            Port = port,
            InitialTargetPath = GetOption(args, "--target")
        };
        var url = $"http://127.0.0.1:{options.Port}";
        Console.WriteLine($"TraceLens UI: {url} (Ctrl+C로 종료)");
        if (!HasFlag(args, "--no-browser"))
        {
            TryOpenBrowser(url);
        }

        try
        {
            return await ServerHost.RunAsync(args, options);
        }
        catch (IOException exception) when (IsAddressInUse(exception))
        {
            // 포트가 이미 쓰이고 있을 때 스택 트레이스를 던지지 않고 doctor와 같은 문구로 안내한다.
            Console.Error.WriteLine($"포트 {options.Port}을(를) 이미 다른 프로세스가 쓰고 있다.");
            Console.Error.WriteLine("다른 TraceLens가 떠 있는지 확인하거나 --port로 다른 번호를 준다.");
            return 1;
        }
    }

    /// <summary>Kestrel이 바인딩에 실패하면 내부 예외에 주소 사용 중(SocketError 10048/98)이 들어온다.</summary>
    private static bool IsAddressInUse(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is System.Net.Sockets.SocketException socket &&
                socket.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<int> AnalyzeAsync(string[] args)
    {
        var target = args.FirstOrDefault(item => !item.StartsWith('-'));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("분석할 .sln, .slnx 또는 .csproj 경로가 필요하다.");
            return 2;
        }

        var asJson = HasFlag(args, "--json");
        var outputDirectory = GetOption(args, "--output");
        var configPath = GetOption(args, "--config");
        if (configPath is not null && !File.Exists(configPath))
        {
            Console.Error.WriteLine($"설정 파일을 찾지 못했다: {configPath}");
            return 2;
        }

        using var provider = BuildAnalyzerServices(asJson);
        var analyzer = provider.GetRequiredService<ITraceLensAnalyzer>();
        var progress = new Progress<AnalysisProgress>(item =>
        {
            if (!asJson)
            {
                Console.WriteLine($"  [{item.Stage}] {item.Message}{(item.Count is null ? string.Empty : $" ({item.Count})")}");
            }
        });

        try
        {
            var result = await analyzer.AnalyzeAsync(
                target, progress, outputDirectory: outputDirectory, configPath: configPath);
            if (asJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new
                    {
                        snapshotId = result.Graph.SnapshotId,
                        inputFingerprint = result.Graph.InputFingerprint,
                        outputPath = result.OutputPath,
                        entryPoints = result.Graph.EntryPoints.Count,
                        nodes = result.Graph.Nodes.Count,
                        edges = result.Graph.Edges.Count,
                        callSites = result.Graph.CallSites.Count,
                        findings = result.FindingCount,
                        elapsedMs = result.ElapsedMs,
                        resolutionRate = result.Graph.Diagnostics?.Calls.ResolutionRate ?? 0,
                        notes = result.Graph.Diagnostics?.EntryPointNotes ?? [],
                        profile = HasFlag(args, "--profile") ? result.Profile : null
                    },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
                return 0;
            }

            PrintAnalyzeSummary(result, target);
            if (HasFlag(args, "--profile"))
            {
                PrintProfile(result.Profile);
            }

            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException or InvalidOperationException)
        {
            Console.Error.WriteLine($"분석에 실패했다: {exception.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 단계별 시간과 최대 메모리를 출력한다. 측정 없이 성능을 고치지 않기 위한 도구다.
    /// 최대 메모리는 이 프로세스 전체 값이라 분석만의 값이 아니다 — 그대로 적는다.
    /// </summary>
    private static void PrintProfile(AnalysisProfile? profile)
    {
        if (profile is null)
        {
            Console.WriteLine("  프로파일 정보가 없다.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("단계별 시간:");
        foreach (var stage in profile.Stages)
        {
            Console.WriteLine($"  {stage.Stage,-12} {stage.ElapsedMs,6}ms");
        }

        Console.WriteLine($"  {"합계",-12} {profile.Stages.Sum(item => item.ElapsedMs),6}ms");
        Console.WriteLine($"  최대 작업 집합 {profile.PeakWorkingSetBytes / (1024 * 1024)}MB (프로세스 전체 값이다)");
    }

    private static void PrintAnalyzeSummary(AnalysisResult result, string targetPath)
    {
        var graph = result.Graph;
        Console.WriteLine();
        Console.WriteLine($"분석을 마쳤다 ({result.ElapsedMs}ms).");
        Console.WriteLine($"  스냅샷: {graph.SnapshotId}");
        Console.WriteLine($"  저장 위치: {result.OutputPath} ({result.GraphSizeBytes / 1024}KB)");
        Console.WriteLine(
            $"  진입점 {graph.EntryPoints.Count}개" +
            $"(컨트롤러 {graph.EntryPoints.Count(item => item.Source == EndpointSource.Controller)} · " +
            $"Minimal API {graph.EntryPoints.Count(item => item.Source == EndpointSource.MinimalApi)})");
        Console.WriteLine($"  클래스 {graph.Classes.Count}개 · 메서드 {graph.Nodes.Count}개 · 호출 엣지 {graph.Edges.Count}개");

        var diagnostics = graph.Diagnostics;
        if (diagnostics is not null)
        {
            Console.WriteLine(
                $"  호출 해석 {diagnostics.Calls.Resolved}/{diagnostics.Calls.Inspected} " +
                $"({diagnostics.Calls.ResolutionRate:P1}) · 외부 호출 {diagnostics.Calls.External}은 분모에서 뺌 · " +
                $"모호 {diagnostics.Calls.Ambiguous} · 미해석 {diagnostics.Calls.Unresolved}");
            foreach (var note in graph.DataAccess?.Notes ?? [])
            {
                Console.WriteLine($"  ! {note}");
            }
            Console.WriteLine($"  프로젝트 {diagnostics.LoadedProjects}/{diagnostics.TotalProjects} (컴파일 오류 {diagnostics.ProjectsWithCompileErrors})");

            // 어느 프로젝트가 왜 실패했는지 이름으로 말한다. 숫자만으로는 무엇을 고쳐야 할지 알 수 없다.
            var failed = diagnostics.Projects
                .Where(item => !item.Loaded || item.ErrorCount > 0)
                .Take(3)
                .ToArray();
            foreach (var project in failed)
            {
                var state = project.Loaded ? $"컴파일 오류 {project.ErrorCount}건" : "로드 실패";
                Console.WriteLine($"  × {project.Name} — {state}");
                if (project.SampleErrors.Count > 0)
                {
                    Console.WriteLine($"      {project.SampleErrors[0]}");
                }
            }

            if (failed.Length > 0)
            {
                Console.WriteLine($"  → `dotnet build \"{targetPath}\"`로 먼저 확인한다. 컴파일 오류가 있는 프로젝트의 호출 관계는 빠질 수 있다.");
            }

            foreach (var note in diagnostics.EntryPointNotes)
            {
                Console.WriteLine($"  ! {note}");
            }

            // 한계는 전부 출력한다. 잘라 내면 "무엇을 보지 않았는지"가 조용히 사라진다.
            foreach (var limitation in diagnostics.Limitations)
            {
                Console.WriteLine($"  - {limitation}");
            }
        }

        PrintTestProjectHint(diagnostics);
        PrintGitIgnoreTip(targetPath);
    }

    /// <summary>
    /// 테스트로 보이는 프로젝트가 섞여 있으면 알린다. 기본 analysisScope=solution이라
    /// 테스트 프로젝트의 타입이 클래스 다이어그램에 함께 나온다.
    /// 판정은 이름 규칙뿐이라 "보인다"로만 쓰고 자동으로 빼지 않는다.
    /// </summary>
    private static void PrintTestProjectHint(AnalysisDiagnostics? diagnostics)
    {
        if (diagnostics is null)
        {
            return;
        }

        var testLike = diagnostics.Projects
            .Where(project => project.Loaded)
            .Count(project =>
                project.Name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
                project.Name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase) ||
                project.Name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase));
        if (testLike > 0)
        {
            Console.WriteLine(
                $"  테스트로 보이는 프로젝트 {testLike}개를 함께 분석했다 → 빼려면 설정에 " +
                "excludeProjectNamePatterns: [\"*.Tests\"]를 둔다(`tracelens init`).");
        }
    }

    /// <summary>
    /// <c>.tracelens/</c>가 분석 대상 저장소 아래에 생긴다. 무시 목록에 없으면 커밋에 섞인다.
    /// 이미 적혀 있으면 아무 말도 하지 않는다.
    /// </summary>
    private static void PrintGitIgnoreTip(string targetPath)
    {
        try
        {
            var directory = Directory.Exists(targetPath)
                ? new DirectoryInfo(targetPath)
                : new FileInfo(targetPath).Directory;
            for (var level = 0; directory is not null && level <= 6; level++, directory = directory.Parent)
            {
                if (!Directory.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    continue;
                }

                var ignorePath = Path.Combine(directory.FullName, ".gitignore");
                var alreadyIgnored = File.Exists(ignorePath) &&
                    File.ReadAllLines(ignorePath).Any(line => line.Trim().TrimStart('/').StartsWith(".tracelens", StringComparison.OrdinalIgnoreCase));
                if (!alreadyIgnored)
                {
                    Console.WriteLine("  팁: .gitignore에 .tracelens/ 를 추가하면 저장소가 깨끗하다.");
                }

                return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 팁 하나를 못 띄우는 것으로 분석 결과를 가릴 이유는 없다.
        }
    }

    /// <summary>
    /// 대상 폴더에 설정 파일을 만든다. 이미 있으면 <b>덮어쓰지 않는다</b> —
    /// 사내 설정을 날리는 것이 설정 파일 하나를 새로 만드는 것보다 훨씬 비싸다.
    /// </summary>
    private static async Task<int> InitAsync(string[] args)
    {
        var target = GetOption(args, "--target") ?? Directory.GetCurrentDirectory();
        if (ResolveConfigDirectory(target) is not { } directory)
        {
            Console.Error.WriteLine($"대상 폴더를 찾을 수 없다: {Path.GetFullPath(target)}");
            return 2;
        }

        var path = Path.Combine(directory, TraceLensConfig.FileName);
        var force = HasFlag(args, "--force");

        if (File.Exists(path) && !force)
        {
            Console.WriteLine($"이미 있다: {path}");
            var existing = await TraceLensConfig.LoadAsync(directory);
            foreach (var issue in existing.Issues)
            {
                Console.WriteLine($"  ! {issue.Message}");
            }

            var changed = DescribeNonDefaults(existing.Config);
            Console.WriteLine(changed.Count == 0
                ? "  기본값과 다른 키: 없음(이 파일은 동작을 바꾸지 않는다)."
                : $"  기본값과 다른 키: {string.Join(", ", changed)}");
            Console.WriteLine("  덮어쓰려면 --force를 준다.");
            return 0;
        }

        // 스키마는 배포물 안의 파일을 가리킨다. 없으면 저장소의 raw 주소를 적어 자동완성이라도 되게 한다.
        var schema = FindSchemaPath() is { } schemaPath
            ? new Uri(schemaPath).AbsoluteUri
            : "https://raw.githubusercontent.com/tracelens/tracelens/main/docs/tracelens.config.schema.json";
        await File.WriteAllTextAsync(path, TraceLensConfig.BuildTemplate(schema));
        Console.WriteLine($"설정 파일을 만들었다: {path}");
        Console.WriteLine("  값은 모두 기본값·빈 배열이라 지금 동작은 그대로다.");
        Console.WriteLine("  DB·캐시 보기가 비어 있다면 db.queryApis에 사내 데이터 래퍼 패턴을 적는다.");
        return 0;
    }

    /// <summary>기본값과 합친 유효 설정 전체를 출력한다. 설정 파일에서 읽은 키도 함께 알려 준다.</summary>
    private static async Task<int> ConfigAsync(string[] args)
    {
        if (!HasFlag(args, "--show"))
        {
            Console.Error.WriteLine("사용법: tracelens config --show [--target <경로>]");
            return 2;
        }

        var target = GetOption(args, "--target") ?? Directory.GetCurrentDirectory();
        if (ResolveConfigDirectory(target) is not { } directory)
        {
            Console.Error.WriteLine($"대상 폴더를 찾을 수 없다: {Path.GetFullPath(target)}");
            return 2;
        }

        var found = TraceLensConfig.FindConfigFile(directory);
        var result = await TraceLensConfig.LoadAsync(
            found is null ? directory : Path.GetDirectoryName(found)!);

        Console.WriteLine(found is null
            ? $"설정 파일이 없다(기본값만 쓴다). 만들려면 `tracelens init --target \"{directory}\"`."
            : $"설정 파일: {found}");
        foreach (var issue in result.Issues)
        {
            Console.WriteLine($"  ! {issue.Message}");
        }

        var changed = DescribeNonDefaults(result.Config);
        Console.WriteLine(changed.Count == 0
            ? "기본값과 다른 키: 없음"
            : $"기본값과 다른 키: {string.Join(", ", changed)}");
        Console.WriteLine();
        Console.WriteLine(TraceLensConfig.SerializeEffective(result.Config));
        return 0;
    }

    /// <summary>
    /// 기본값과 다른 최상위 키 이름. "내 설정이 먹었는가"의 답이다.
    /// 값 자체는 유효 설정 JSON에 그대로 나오므로 여기서는 이름만 센다.
    /// </summary>
    private static IReadOnlyList<string> DescribeNonDefaults(TraceLensConfig config)
    {
        var defaults = TraceLensConfig.Default;
        var changed = new List<string>();
        void Check(string name, bool differs)
        {
            if (differs)
            {
                changed.Add(name);
            }
        }

        Check("entryPointAttributes", !config.EntryPointAttributes.SequenceEqual(defaults.EntryPointAttributes, StringComparer.Ordinal));
        Check("maxCallDepth", config.MaxCallDepth != defaults.MaxCallDepth);
        Check("excludeNamespacePrefixes", !config.ExcludeNamespacePrefixes.SequenceEqual(defaults.ExcludeNamespacePrefixes, StringComparer.Ordinal));
        Check("analysisScope", config.AnalysisScope != defaults.AnalysisScope);
        Check("excludeProjectNamePatterns", config.ExcludeProjectNamePatterns.Count != 0);
        Check("includeObjectCreation", config.IncludeObjectCreation != defaults.IncludeObjectCreation);
        Check("includeMembers", config.IncludeMembers != defaults.IncludeMembers);
        Check("includeInterfaces", config.IncludeInterfaces != defaults.IncludeInterfaces);
        Check("includeStartupFlow", config.IncludeStartupFlow != defaults.IncludeStartupFlow);
        Check("includeDataAccess", config.IncludeDataAccess != defaults.IncludeDataAccess);
        Check("includeMiddleware", config.IncludeMiddleware != defaults.IncludeMiddleware);
        Check("includeCallSites", config.IncludeCallSites != defaults.IncludeCallSites);
        Check("maxCallSitesPerEdge", config.MaxCallSitesPerEdge != defaults.MaxCallSitesPerEdge);
        Check("maxBoundaryCallSites", config.MaxBoundaryCallSites != defaults.MaxBoundaryCallSites);
        Check("db.queryApis", !config.Db.QueryApis.SequenceEqual(defaults.Db.QueryApis, StringComparer.Ordinal));
        Check("db.cacheApis", !config.Db.CacheApis.SequenceEqual(defaults.Db.CacheApis, StringComparer.Ordinal));
        Check("db.connectionFactories", !config.Db.ConnectionFactories.SequenceEqual(defaults.Db.ConnectionFactories, StringComparer.Ordinal));
        Check("db.dataSources", config.Db.DataSources.Count != 0);
        Check("db.cacheWrappers", config.Db.CacheWrappers.Count != 0);
        return changed;
    }

    /// <summary>
    /// 설정 파일을 둘 폴더. 폴더를 주면 그대로, <c>.sln</c>/<c>.csproj</c>를 주면 그 파일이 있는 폴더다.
    /// 둘 다 아니면 null이다 — 없는 폴더의 부모에 조용히 쓰면 "만들었다는데 없다"가 된다.
    /// </summary>
    private static string? ResolveConfigDirectory(string target)
    {
        var fullPath = Path.GetFullPath(target);
        if (Directory.Exists(fullPath))
        {
            return fullPath;
        }

        if (File.Exists(fullPath) && Path.GetDirectoryName(fullPath) is { Length: > 0 } parent)
        {
            return parent;
        }

        return null;
    }

    /// <summary>배포물·저장소에서 스키마 파일을 찾는다. 없으면 null이다.</summary>
    private static string? FindSchemaPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; directory is not null && level <= 8; level++, directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "docs", "tracelens.config.schema.json");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static async Task<int> McpAsync(string[] args)
    {
        try
        {
            return await McpHost.RunAsync(args, GetOption(args, "--graph"));
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static async Task<int> Doctor(string[] args)
    {
        var report = new EnvironmentDiagnostics().Run(new DiagnosticsOptions
        {
            TargetPath = GetOption(args, "--target"),
            Port = TryGetInt(args, "--port") ?? ServerHost.DefaultPort,
            McpHostCandidates = McpConfigWriter.GetHostCandidates()
        }) with
        {
            // 500이 났을 때 무엇을 첨부해야 하는지 알 수 있게 로그 위치를 함께 알려 준다.
            Log = new DiagnosticsLogInfo(ServerHost.LogFilePath ?? FileLoggerOptions.DefaultDirectory, ServerHost.RecentErrors)
        };

        if (HasFlag(args, "--json"))
        {
            // 유효 설정을 함께 담는다. "내 설정이 먹었는지"를 doctor 한 번으로 확인할 수 있어야 한다.
            var configDirectory = ResolveConfigDirectory(GetOption(args, "--target") ?? Directory.GetCurrentDirectory());
            var effective = configDirectory is null
                ? TraceLensConfig.Default
                : (await TraceLensConfig.LoadAsync(
                    TraceLensConfig.FindConfigFile(configDirectory) is { } found
                        ? Path.GetDirectoryName(found)!
                        : configDirectory)).Config;
            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    report.CanAnalyze,
                    report.ToolVersion,
                    report.RuntimeVersion,
                    report.OperatingSystem,
                    report.SdkPath,
                    report.SupportedTargets,
                    report.UnsupportedTargets,
                    report.Items,
                    report.Log,
                    effectiveConfig = JsonSerializer.Deserialize<JsonElement>(
                        TraceLensConfig.SerializeEffective(effective))
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            return report.CanAnalyze ? 0 : 1;
        }

        Console.WriteLine($"TraceLens {report.ToolVersion}");
        // 서버가 아직 안 떴으면 LogFilePath가 없고 폴더만 안다. 파일처럼 보여 주면 찾으러 갔다 헛걸음한다.
        Console.WriteLine(ServerHost.LogFilePath is { Length: > 0 } logFile
            ? $"  로그 파일: {logFile}"
            : $"  로그 폴더: {FileLoggerOptions.DefaultDirectory}");
        Console.WriteLine($"  런타임: {report.RuntimeVersion}");
        Console.WriteLine($"  운영체제: {report.OperatingSystem}");
        Console.WriteLine($"  지원 대상: {string.Join(", ", report.SupportedTargets)}");
        Console.WriteLine();
        foreach (var item in report.Items)
        {
            var mark = item.Severity switch
            {
                DiagnosticSeverity.Error => "X",
                DiagnosticSeverity.Warning => "!",
                _ => "o"
            };
            Console.WriteLine($"  [{mark}] {item.Code}: {item.Message}");
            if (!string.IsNullOrEmpty(item.SuggestedAction))
            {
                Console.WriteLine($"        → {item.SuggestedAction}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(report.CanAnalyze
            ? "분석을 실행할 수 있는 상태다."
            : "분석을 실행할 수 없다. 위의 X 항목을 먼저 해결해야 한다.");
        return report.CanAnalyze ? 0 : 1;
    }

    private static async Task<int> DemoAsync(string[] args)
    {
        // 설치 폴더(C:\Program Files\...)에서 실행해도 쓰기 권한 문제가 없도록 사용자 영역을 기본값으로 쓴다.
        // UI가 쓰는 위치와 같다. --dir을 주면 그대로 존중한다.
        var destination = GetOption(args, "--dir") ?? DemoSamples.DefaultDestination("demo");
        var samples = DemoSamples.Find();
        if (samples.Count == 0)
        {
            Console.Error.WriteLine("예제를 찾을 수 없다. 배포 패키지에 samples 폴더가 들어 있어야 한다.");
            if (DemoSamples.LastPathIssue is { Length: > 0 } issue)
            {
                Console.Error.WriteLine($"  {issue}");
            }
            return 1;
        }

        Console.WriteLine("TraceLens 예제");
        // 같은 소스 폴더를 쓰는 항목은 "같은 프로젝트의 학습 경로"다. 프로젝트가 여러 개인 것처럼 보이지 않게 들여쓴다.
        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)
        {
            var isNewProject = seenSources.Add(sample.SourceDirectory);
            var indent = isNewProject ? "  - " : "      · ";
            Console.WriteLine($"{indent}{sample.Id}: {sample.Title}");
            Console.WriteLine($"      {sample.Description}");
            foreach (var line in sample.Expected)
            {
                Console.WriteLine($"        보이는 것: {line}");
            }

            for (var step = 0; step < sample.LearningSteps.Count; step++)
            {
                Console.WriteLine($"        {step + 1}) {sample.LearningSteps[step]}");
            }
        }

        Console.WriteLine();
        var copyResult = await DemoSamples.CopyAsync(samples, destination);
        var target = Path.Combine(copyResult.Destination, samples[0].RelativeTargetPath);
        Console.WriteLine($"예제를 복사했다: {copyResult.Destination} (프로젝트 {copyResult.CopiedDirectories.Count}개)");
        Console.WriteLine("다음 순서로 해 보면 된다:");
        Console.WriteLine($"  0. tracelens doctor --target \"{target}\"");
        Console.WriteLine($"  1. tracelens analyze \"{target}\"");
        Console.WriteLine($"  2. tracelens serve --target \"{target}\"");
        Console.WriteLine("  3. 진입점을 고르고 → 소스 근거를 확인하고 → AI 문맥을 복사한다.");
        Console.WriteLine("  4. AI 에이전트 연결: UI의 \"MCP 연동 설정 보기\" 또는 docs/mcp-setup_kr.md");
        return 0;
    }

    private static ServiceProvider BuildAnalyzerServices(bool quiet)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options => options.SingleLine = true);
            builder.SetMinimumLevel(quiet ? LogLevel.Error : LogLevel.Warning);
        });
        services.AddTraceLensAnalyzer();
        services.AddTraceLensContext();
        return services.BuildServiceProvider();
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.WriteLine($"브라우저를 자동으로 열지 못했다. 직접 열면 된다: {url}");
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int? TryGetInt(string[] args, string name) =>
        int.TryParse(GetOption(args, name), out var value) ? value : null;

    private static bool HasFlag(string[] args, string name) =>
        args.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
}

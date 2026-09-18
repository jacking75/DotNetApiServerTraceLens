using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;
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
            "mcp" => await McpAsync(rest),
            "doctor" => Doctor(rest),
            "demo" => await DemoAsync(rest),
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
              tracelens analyze <solution-or-project-path> [--json] [--profile]
                  한 번 분석하고 결과 요약을 출력한다.
                  결과는 대상과 참조 프로젝트의 공통 상위 폴더의 .tracelens/graph.json에 저장된다(출력 위치는 analyze 결과의 outputPath).
                  --profile을 주면 단계별 시간과 최대 메모리를 함께 출력한다.
              tracelens mcp --graph <graph-path>
                  로컬 stdio MCP 서버를 띄운다. AI 에이전트가 이 분석 결과를 읽는다.
              tracelens doctor [--target <경로>] [--json]
                  실행 환경을 점검하고 실패 원인과 다음 행동을 알려준다. 설치·설정 변경은 하지 않는다.
              tracelens demo [--dir <복사할 위치>]
                  예제 프로젝트를 원하는 폴더에 복사하고 분석 방법을 안내한다.

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
            Console.Error.WriteLine("분석할 .sln 또는 .csproj 경로가 필요하다.");
            return 2;
        }

        var asJson = HasFlag(args, "--json");
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
            var result = await analyzer.AnalyzeAsync(target, progress);
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

        PrintGitIgnoreTip(targetPath);
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

    private static int Doctor(string[] args)
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
            Console.WriteLine(JsonSerializer.Serialize(
                report,
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

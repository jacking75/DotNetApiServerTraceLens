using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 분석 진행 상태. 근거 없는 진행률(%) 대신 단계와 실제 처리 건수를 보여준다.
/// </summary>
/// <param name="Stage">load · entrypoints · scan · rules · save 중 하나.</param>
public sealed record AnalysisProgress(string Stage, string Message, int? Count = null);

/// <param name="ElapsedMs">이 단계에만 쓴 시간. 단계는 순서대로 실행되므로 합이 전체와 거의 같다.</param>
public sealed record AnalysisStageTiming(string Stage, long ElapsedMs);

/// <param name="Stages">단계별 시간(load·entrypoints·scan·rules·save).</param>
/// <param name="PeakWorkingSetBytes">이 프로세스가 분석 중에 쓴 최대 작업 집합. 분석 전 값과 비교한 것이 아니라 프로세스 전체 값이다.</param>
public sealed record AnalysisProfile(IReadOnlyList<AnalysisStageTiming> Stages, long PeakWorkingSetBytes);

/// <summary>단계별 시간을 재는 작은 도우미. <c>--profile</c>이 없어도 비용이 거의 없다.</summary>
internal sealed class StageTimer
{
    private readonly List<AnalysisStageTiming> stages = [];
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private long lastMs;

    public void Mark(string stage)
    {
        var now = stopwatch.ElapsedMilliseconds;
        stages.Add(new AnalysisStageTiming(stage, now - lastMs));
        lastMs = now;
    }

    public AnalysisProfile Build() => new(
        stages,
        System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64);
}

public interface ITraceLensAnalyzer
{
    /// <param name="targetPath">분석 대상 .sln 또는 .csproj 경로.</param>
    /// <param name="progress">단계별 진행 상태를 받을 곳. null이면 보고하지 않는다.</param>
    Task<AnalysisResult> AnalyzeAsync(
        string targetPath,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class TraceLensAnalyzer(
    SolutionLoader solutionLoader,
    EntryPointScanner entryPointScanner,
    DiResolver diResolver,
    SolutionScanner solutionScanner,
    CallGraphWalker callGraphWalker,
    SnapshotHasher snapshotHasher,
    AnalysisManifestBuilder manifestBuilder,
    StartupFlowScanner startupFlowScanner,
    DataAccessScanner dataAccessScanner,
    CompensationAnalyzer compensationAnalyzer,
    DataRuleEngine dataRuleEngine,
    MiddlewareScanner middlewareScanner,
    ILogger<TraceLensAnalyzer> logger) : ITraceLensAnalyzer
{
    /// <summary>분석기 버전. 스냅샷 비교의 전제 조건(같은 분석기로 만든 결과인지) 확인에 쓴다.</summary>
    public static string AnalyzerVersion { get; } =
        typeof(TraceLensAnalyzer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(TraceLensAnalyzer).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>DB·미들웨어 규칙 카탈로그의 버전. 규칙이 바뀌면 점검 비교 조건이 달라진다.</summary>
    public const string RulesVersion = "1";

    /// <summary>
    /// 같은 대상의 동시 분석을 직렬화한다. 부분 JSON이 정상 그래프로 읽히는 것을 막는 첫 번째 방어선이다.
    /// 대상이 다르면 서로 막지 않는다.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> AnalysisGates =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<AnalysisResult> AnalyzeAsync(
        string targetPath,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var gate = AnalysisGates.GetOrAdd(Path.GetFullPath(targetPath), _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            logger.LogInformation("같은 대상의 분석이 진행 중이라 끝날 때까지 기다린다: {Target}", targetPath);
            await gate.WaitAsync(cancellationToken);
        }

        try
        {
            return await AnalyzeCoreAsync(targetPath, progress, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AnalysisResult> AnalyzeCoreAsync(
        string targetPath,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var stageTimer = new StageTimer();
        var startedAtUtc = DateTime.UtcNow;
        var fullTargetPath = Path.GetFullPath(targetPath);
        var targetDirectory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new ArgumentException("분석 대상 디렉터리를 확인할 수 없다.", nameof(targetPath));

        progress?.Report(new AnalysisProgress("load", "프로젝트를 여는 중이다."));
        await using var loaded = await solutionLoader.LoadAsync(fullTargetPath, cancellationToken);
        progress?.Report(new AnalysisProgress("load", "프로젝트를 열었다.", loaded.AnalyzableProjectIds.Count));
        var solutionDirectory = ResolveRootDirectory(targetDirectory, loaded);
        if (!string.Equals(solutionDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("참조 프로젝트까지 포함하도록 분석 루트를 {Root}로 잡았다.", solutionDirectory);
        }

        var configResult = await TraceLensConfig.LoadAsync(solutionDirectory, cancellationToken);
        var config = configResult.Config;
        foreach (var issue in configResult.Issues)
        {
            // 설정 문제로 분석을 멈추지 않는다. 무엇을 기본값으로 대체했는지 로그와 진단에 남긴다.
            logger.LogWarning("tracelens.config.json: {Message}", issue.Message);
        }
        // 수집기 다섯 개가 같은 문서를 차례로 바인딩한다. 시맨틱 모델을 미리 병렬로 만들어 한 번만 하게 한다.
        progress?.Report(new AnalysisProgress("load", "구문 트리와 시맨틱 모델을 준비하는 중이다."));
        await loaded.PrewarmSemanticsAsync(PrewarmDocumentLimit, cancellationToken);
        if (loaded.PrewarmedDocuments == 0)
        {
            logger.LogInformation(
                "문서가 {Limit}개를 넘어 시맨틱 모델을 미리 만들지 않는다. 결과는 같고 시간만 더 걸린다.",
                PrewarmDocumentLimit);
        }

        await diResolver.BuildAsync(loaded, cancellationToken);
        stageTimer.Mark("load");
        progress?.Report(new AnalysisProgress("entrypoints", "HTTP 진입점을 찾는 중이다."));
        var entryPointScan = await entryPointScanner.ScanAsync(loaded, config, solutionDirectory, cancellationToken);
        var entryPoints = entryPointScan.EntryPoints;
        progress?.Report(new AnalysisProgress("entrypoints", "진입점을 찾았다.", entryPoints.Count));

        var isSolutionScope = config.AnalysisScope == AnalysisScope.Solution;
        var handlerCallables = entryPoints
            .Select(item => item.Handler)
            .Where(item => item.Kind is CallableKind.Lambda or CallableKind.LocalFunction)
            .DistinctBy(item => item.NodeId, StringComparer.Ordinal)
            .ToArray();
        stageTimer.Mark("entrypoints");
        progress?.Report(new AnalysisProgress("scan", "타입과 호출을 훑는 중이다."));
        var scan = await solutionScanner.ScanAsync(
            loaded,
            config,
            solutionDirectory,
            isSolutionScope,
            handlerCallables,
            cancellationToken);
        progress?.Report(new AnalysisProgress("scan", "클래스를 모았다.", scan.Classes.Count));

        IReadOnlyList<GraphNode> nodes;
        IReadOnlyList<GraphEdge> edges;
        IReadOnlyList<CallSite> callSites;
        CallResolutionStats stats;
        var depthLimitReached = false;
        var droppedCallSites = scan.DroppedCallSites;
        if (isSolutionScope)
        {
            (nodes, edges, callSites, stats) = (scan.Nodes, scan.Edges, scan.CallSites, scan.Stats);
        }
        else
        {
            var walk = await callGraphWalker.WalkAsync(loaded, entryPoints, config, solutionDirectory, cancellationToken);
            (nodes, edges, callSites, stats) = (walk.Nodes, walk.Edges, walk.CallSites, walk.Stats);
            depthLimitReached = walk.DepthLimitReached;
            droppedCallSites = walk.DroppedCallSites;
        }

        var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var reachableEntryPoints = entryPoints
            .Select(item => item.EntryPoint)
            .Where(entryPoint => nodeIds.Contains(entryPoint.NodeId))
            .ToArray();

        var manifestResult = await manifestBuilder.BuildAsync(loaded, fullTargetPath, solutionDirectory, cancellationToken);
        // 노드·클래스가 없는 파일(시작 흐름만 있는 Program.cs 등)도 신선도 대상에 들어가도록 분석한 소스 전체를 해시한다.
        var fileHashes = await snapshotHasher.ComputeAsync(
            solutionDirectory,
            manifestResult.SourceFiles
                .Concat(nodes.Select(node => node.File))
                .Concat(scan.Classes.Select(item => item.File)),
            cancellationToken);
        progress?.Report(new AnalysisProgress("scan", "메서드 노드를 모았다.", nodes.Count));
        stageTimer.Mark("scan");
        progress?.Report(new AnalysisProgress("rules", "DB·캐시와 미들웨어 규칙을 확인하는 중이다."));

        // 시작 흐름 스캔과 DB·캐시 스캔은 서로를 읽지 않는다. Roslyn Solution은 불변이라 함께 돌려도 안전하다.
        var startupFlowTask = startupFlowScanner.ScanAllAsync(loaded, config, solutionDirectory, cancellationToken);
        Task<DataAccessScanResult?> dataScanTask = config.IncludeDataAccess
            ? ScanDataAccessAsync()
            : Task.FromResult<DataAccessScanResult?>(null);

        async Task<DataAccessScanResult?> ScanDataAccessAsync() =>
            await dataAccessScanner.ScanAsync(loaded, config, solutionDirectory, cancellationToken);

        await Task.WhenAll(startupFlowTask, dataScanTask);

        var startupFlows = await startupFlowTask;
        var startupFlow = startupFlows.Count > 0 ? startupFlows[0] : null;
        var dataScan = await dataScanTask;
        DataAccessDocument? dataAccess = null;
        if (dataScan is not null)
        {
            var effects = compensationAnalyzer.BuildEffects(
                dataScan.Methods, dataScan.MethodDataSources, cancellationToken);
            var writePaths = compensationAnalyzer.BuildWritePaths(
                dataScan.Methods, effects, dataScan.MethodDataSources, config, cancellationToken);
            dataAccess = dataRuleEngine.Build(
                dataScan, writePaths, effects, startupFlow, config, cancellationToken);
        }

        var middlewarePipelines = await middlewareScanner.ScanAllAsync(
            loaded, config, solutionDirectory, startupFlow, dataScan, cancellationToken);
        var middleware = middlewarePipelines.Count > 0 ? middlewarePipelines[0] : null;
        progress?.Report(new AnalysisProgress(
            "rules",
            "점검을 마쳤다.",
            (dataAccess?.Findings.Count ?? 0) + middlewarePipelines.Sum(item => item.Findings.Count)));

        stageTimer.Mark("rules");
        var changedDuringAnalysis = FindChangedInputs(
            solutionDirectory, manifestResult, fileHashes.Keys, startedAtUtc);
        // 호스트 구성을 못 찾으면 로그만 남기고 끝내면 안 된다. 두 보기가 비는 이유가 결과에 남아야 한다.
        var hostLimitations = startupFlows.Count == 0 && middlewarePipelines.Count == 0 && config.IncludeStartupFlow
            ? new[]
            {
                "Program.cs/Startup.cs에서 호스트 구성을 찾지 못했다. 시작 흐름과 미들웨어 보기가 비는 이유다."
            }
            : [];

        var diagnostics = BuildDiagnostics(
            loaded,
            config,
            stats,
            manifestResult,
            entryPointScan.Notes,
            entryPointScan.Candidates,
            hostLimitations,
            depthLimitReached,
            droppedCallSites,
            changedDuringAnalysis,
            configResult.Issues);

        var inputFingerprint = SnapshotHasher.Fold(
            fileHashes.Select(entry => entry.Key + "=" + entry.Value)
                .Concat(manifestResult.Manifest.Inputs.Select(item => item.Path + "=" + item.Hash))
                .Append("analyzer=" + AnalyzerVersion)
                .Append("rules=" + RulesVersion)
                .Append("scope=" + config.AnalysisScope));
        var analyzedAt = DateTimeOffset.Now;
        var snapshotId = CreateSnapshotId(analyzedAt, inputFingerprint);

        var document = new GraphDocument(
            GraphDocument.CurrentSchemaVersion,
            fullTargetPath,
            analyzedAt,
            reachableEntryPoints,
            scan.Classes,
            nodes,
            edges,
            fileHashes)
        {
            RootDirectory = solutionDirectory,
            StartupFlow = startupFlow,
            StartupFlows = startupFlows,
            DataAccess = dataAccess,
            Middleware = middleware,
            MiddlewarePipelines = middlewarePipelines,
            SnapshotId = snapshotId,
            InputFingerprint = inputFingerprint,
            Manifest = manifestResult.Manifest,
            Diagnostics = diagnostics,
            CallSites = callSites,
            AnalyzerVersion = AnalyzerVersion,
            RulesVersion = RulesVersion
        };

        var outputDirectory = Path.Combine(solutionDirectory, ".tracelens");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "graph.json");
        var json = JsonSerializer.Serialize(document, GraphDocument.JsonOptions);
        progress?.Report(new AnalysisProgress("save", "graph.json을 저장하는 중이다.", json.Length / 1024));
        await WriteAtomicallyAsync(outputPath, json, cancellationToken);

        // members가 들어가면서 커진 만큼, 언제 includeMembers를 꺼야 할지 판단할 수 있게 크기를 남긴다.
        var sizeKb = json.Length / 1024;
        if (sizeKb > GraphSizeWarningKb)
        {
            logger.LogWarning(
                "graph.json이 {SizeKb}KB다. 화면이 느리면 tracelens.config.json의 includeMembers를 false로 두면 줄어든다.",
                sizeKb);
        }
        else
        {
            logger.LogInformation("graph.json {SizeKb}KB · 멤버 {MemberCount}개(수집 {MemberElapsedMs}ms).", sizeKb, scan.MemberCount, scan.MemberElapsedMs);
        }

        stopwatch.Stop();
        stageTimer.Mark("save");
        return new AnalysisResult(
            document,
            stopwatch.ElapsedMilliseconds,
            outputPath,
            scan.MemberCount,
            scan.MemberElapsedMs,
            json.Length,
            (dataAccess?.Findings.Count ?? 0) + middlewarePipelines.Sum(item => item.Findings.Count))
        {
            Profile = stageTimer.Build()
        };
    }

    /// <summary>
    /// 임시 파일에 완전히 쓰고, 다시 읽어 파싱되는지 확인한 다음에야 기존 파일을 교체한다.
    /// 실패하면 이전 정상 그래프가 그대로 남는다.
    /// </summary>
    private static async Task WriteAtomicallyAsync(string outputPath, string json, CancellationToken cancellationToken)
    {
        var temporaryPath = outputPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        try
        {
            await using (var stream = File.OpenRead(temporaryPath))
            {
                _ = await JsonSerializer.DeserializeAsync<GraphDocument>(stream, GraphDocument.JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("새로 만든 graph.json을 다시 읽지 못했다.");
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 임시 파일을 못 지워도 분석 결과에는 영향이 없다.
        }
    }

    /// <summary>분석 시작 이후에 수정된 입력 파일을 찾는다. 새 분석을 자동으로 다시 시도하지는 않는다.</summary>
    private static IReadOnlyList<string> FindChangedInputs(
        string rootDirectory,
        AnalysisManifestResult manifestResult,
        IEnumerable<string> hashedFiles,
        DateTime startedAtUtc)
    {
        var changed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var relativePath in hashedFiles.Concat(manifestResult.Manifest.Inputs.Select(item => item.Path)))
        {
            var fullPath = Path.Combine(rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (File.Exists(fullPath) && File.GetLastWriteTimeUtc(fullPath) > startedAtUtc)
                {
                    changed.Add(relativePath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                changed.Add(relativePath);
            }
        }

        return changed.ToArray();
    }

    private static AnalysisDiagnostics BuildDiagnostics(
        LoadedSolution loaded,
        TraceLensConfig config,
        CallResolutionStats stats,
        AnalysisManifestResult manifestResult,
        IReadOnlyList<string> entryPointNotes,
        IReadOnlyList<EntryPointCandidateGroup> entryPointCandidates,
        IReadOnlyList<string> hostLimitations,
        bool depthLimitReached,
        int droppedCallSites,
        IReadOnlyList<string> changedDuringAnalysis,
        IReadOnlyList<ConfigIssue> configIssues)
    {
        var limitations = new List<string>(manifestResult.Manifest.Limitations);
        limitations.AddRange(hostLimitations);
        limitations.AddRange(configIssues.Select(issue => $"설정: {issue.Message}"));
        if (droppedCallSites > 0)
        {
            limitations.Add($"호출 근거 {droppedCallSites}건은 상한(엣지당 {config.MaxCallSitesPerEdge}건, 경계 {config.MaxBoundaryCallSites}건) 때문에 목록에서 뺐다.");
        }

        if (depthLimitReached)
        {
            limitations.Add($"maxCallDepth({config.MaxCallDepth})에 도달해 더 내려가지 않은 경로가 있다.");
        }

        if (config.AnalysisScope == AnalysisScope.EntryPoints)
        {
            limitations.Add("analysisScope=entryPoints라 진입점에서 도달하지 못하는 메서드는 그래프에 없다.");
        }

        limitations.AddRange(loaded.WorkspaceFailures.Take(5).Select(message => "MSBuild: " + message));

        // 인식은 했지만 해석하지 않은 프레임워크를 한 줄씩 남긴다. 결과의 빈칸을 사용자가 설명할 수 있어야 한다.
        limitations.AddRange(UnreadFrameworks.Describe(stats.ExternalByNamespace));

        var projects = loaded.ProjectDiagnostics;
        return new AnalysisDiagnostics(
            projects.Count,
            projects.Count(item => item.Loaded),
            projects.Count(item => item.ErrorCount > 0),
            projects,
            stats,
            config.ExcludeNamespacePrefixes,
            config.MaxCallDepth,
            depthLimitReached,
            config.AnalysisScope.ToString().ToLowerInvariant(),
            limitations)
        {
            ChangedDuringAnalysis = changedDuringAnalysis.Count > 0,
            ChangedDuringAnalysisFiles = changedDuringAnalysis,
            EntryPointNotes = entryPointNotes,
            EntryPointCandidates = entryPointCandidates
        };
    }

    /// <summary>읽을 수 있으면서 실행마다 다른 값. 앞은 시각, 뒤는 입력 지문의 앞부분이다.</summary>
    private static string CreateSnapshotId(DateTimeOffset analyzedAt, string inputFingerprint)
    {
        var stamp = analyzedAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");
        var digest = inputFingerprint.StartsWith("sha256:", StringComparison.Ordinal)
            ? inputFingerprint[7..15]
            : Guid.NewGuid().ToString("N")[..8];
        return $"{stamp}-{digest}";
    }

    /// <summary>이 크기를 넘으면 <c>includeMembers</c>를 끄라고 안내한다.</summary>
    /// <summary>
    /// 시맨틱 모델을 미리 만들어 둘 문서 수 상한. 넘으면 준비 단계를 건너뛴다(결과는 같고 시간만 더 걸린다).
    /// 모델을 전부 강하게 붙들고 있으면 메모리가 문서 수에 비례해 늘기 때문이다.
    /// </summary>
    private const int PrewarmDocumentLimit = 3000;

    private const int GraphSizeWarningKb = 500;

    /// <summary>
    /// 상대 경로와 출력의 기준이 될 디렉터리를 정한다.
    /// .sln이면 솔루션 디렉터리 그대로이고, .csproj면 참조 프로젝트까지 담도록 공통 상위 디렉터리로 올라간다.
    /// 드라이브 루트까지 올라가는 경우에는 범위가 지나치게 넓어지므로 대상 디렉터리를 그대로 쓴다.
    /// </summary>
    private static string ResolveRootDirectory(string targetDirectory, LoadedSolution loaded)
    {
        var root = targetDirectory;
        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            var projectDirectory = Path.GetDirectoryName(project.FilePath);
            if (string.IsNullOrEmpty(projectDirectory))
            {
                continue;
            }

            var common = FindCommonAncestor(root, Path.GetFullPath(projectDirectory));
            if (common is null || string.Equals(common, Path.GetPathRoot(common), StringComparison.OrdinalIgnoreCase))
            {
                return targetDirectory;
            }

            root = common;
        }

        return root;
    }

    /// <summary>두 디렉터리의 공통 상위 디렉터리를 돌려주고, 드라이브가 달라 공통 조상이 없으면 null을 돌려준다.</summary>
    private static string? FindCommonAncestor(string left, string right)
    {
        var current = new DirectoryInfo(left);
        while (current is not null)
        {
            var relative = Path.GetRelativePath(current.FullName, right);
            if (!Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}

/// <param name="MemberCount">클래스 다이어그램용으로 모은 멤버 수.</param>
/// <param name="MemberElapsedMs">멤버 수집에만 쓴 시간(ms). 전체 <paramref name="ElapsedMs"/> 안에 포함된다.</param>
/// <param name="GraphSizeBytes">저장한 <c>graph.json</c>의 크기.</param>
/// <param name="FindingCount">DB·캐시와 미들웨어 점검에서 잡은 항목 수.</param>
public sealed record AnalysisResult(
    GraphDocument Graph,
    long ElapsedMs,
    string OutputPath,
    int MemberCount,
    long MemberElapsedMs,
    int GraphSizeBytes,
    int FindingCount)
{
    /// <summary>단계별 시간과 최대 메모리. <c>analyze --profile</c>이 이 값을 출력한다.</summary>
    public AnalysisProfile? Profile { get; init; }
}

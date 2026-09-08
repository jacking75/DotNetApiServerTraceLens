using System.Text.Json;
using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;
using TraceLens.Context;

namespace TraceLens.Server.Endpoints;

public static class AnalyzeEndpoints
{
    public static RouteGroupBuilder MapAnalyzeEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/analyze", AnalyzeAsync);
        group.MapPost("/analyze/load", LoadSavedAsync);
        group.MapPost("/analyze/import", ImportAsync);
        group.MapPost("/analyze/adopt", AdoptOfflineAsync);
        return group;
    }

    internal static async Task<IResult> AnalyzeAsync(
        AnalyzeRequest request,
        ITraceLensAnalyzer analyzer,
        IGraphStore graphStore,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SolutionPath))
        {
            return ApiResults.LegacyError(StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, "solutionPath가 필요하다.");
        }

        try
        {
            var previousAnalyzedAt = graphStore.Current?.AnalyzedAt;
            var result = await analyzer.AnalyzeAsync(request.SolutionPath, null, cancellationToken);
            graphStore.Set(result.Graph);
            return Results.Ok(BuildSummary(result) with { PreviousAnalyzedAt = previousAnalyzedAt });
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException or InvalidOperationException)
        {
            return ApiResults.LegacyError(StatusCodes.Status400BadRequest, ApiResults.AnalyzeFailed, exception.Message);
        }
    }

    /// <summary>
    /// 다시 분석하지 않고 이전에 저장한 <c>.tracelens/graph.json</c>을 그대로 불러온다.
    /// 서버를 재시작해도(예: run.ps1을 다시 실행) 마지막 분석 결과를 Roslyn 재분석 없이 빠르게 되살리는 용도다.
    /// </summary>
    internal static async Task<IResult> LoadSavedAsync(
        AnalyzeRequest request,
        IGraphStore graphStore,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SolutionPath))
        {
            return ApiResults.LegacyError(StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, "solutionPath가 필요하다.");
        }

        string graphPath;
        try
        {
            var fullTargetPath = Path.GetFullPath(request.SolutionPath);
            var solutionDirectory = Path.GetDirectoryName(fullTargetPath)
                ?? throw new ArgumentException("분석 대상 디렉터리를 확인할 수 없다.", nameof(request));
            graphPath = Path.Combine(solutionDirectory, ".tracelens", "graph.json");
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return ApiResults.LegacyError(StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, exception.Message);
        }

        if (!File.Exists(graphPath))
        {
            return ApiResults.LegacyError(StatusCodes.Status404NotFound, ApiResults.NoAnalysis, "저장된 분석 결과가 없다. 먼저 분석해야 한다.");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var loaded = await GraphJsonStore.LoadAsync(graphPath, cancellationToken);
        stopwatch.Stop();
        if (!loaded.Ok || loaded.Graph is null)
        {
            return ApiResults.LegacyError(
                StatusCodes.Status400BadRequest,
                "GRAPH_INVALID",
                "저장된 분석 결과를 읽을 수 없다: " + string.Join(", ", loaded.Issues.Select(issue => issue.Message)));
        }

        graphStore.Set(loaded.Graph);
        return Results.Ok(BuildResponse(
            loaded.Graph, (int)new FileInfo(graphPath).Length, stopwatch.ElapsedMilliseconds, loaded.Issues));
    }

    /// <summary>
    /// 사용자가 직접 고른 파일(사용자가 원하는 위치에 "저장"해 둔 그래프 JSON)을 불러온다.
    /// 가져온 결과는 <c>offlineSnapshot</c>으로 취급해서 로컬 소스를 읽지 않고,
    /// 대상 폴더의 <c>graph.json</c>도 묵시적으로 덮어쓰지 않는다.
    /// </summary>
    internal static async Task<IResult> ImportAsync(
        HttpContext context,
        IGraphStore graphStore,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(context.Request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);
        var loaded = GraphJsonStore.Parse(json);
        if (!loaded.Ok || loaded.Graph is null)
        {
            return ApiResults.LegacyError(
                StatusCodes.Status400BadRequest,
                "GRAPH_INVALID",
                "올바른 분석 결과 파일이 아니다: " + string.Join(", ", loaded.Issues.Select(issue => issue.Message)));
        }

        // 가져온 파일은 이 기계 안의 보관 위치에만 쓴다. 활성 graph.json은 건드리지 않는다.
        var offlinePath = Path.Combine(
            Path.GetTempPath(), "tracelens-imported", $"{DateTime.UtcNow:yyyyMMddHHmmss}", "graph.json");
        await GraphJsonStore.WriteAtomicallyAsync(offlinePath, loaded.Graph, cancellationToken);

        graphStore.Set(loaded.Graph, GraphOrigin.OfflineSnapshot, offlinePath);
        return Results.Ok(BuildResponse(loaded.Graph, json.Length, elapsedMs: 0, loaded.Issues) with
        {
            Origin = "offlineSnapshot",
            Notice = "가져온 분석 결과다. 원본 루트를 연결하기 전에는 소스 보기와 MCP 소스 조회를 쓸 수 없다."
        });
    }

    /// <summary>
    /// 가져온 결과의 원본 루트를 연결한다. 입력 지문이 일치할 때만 신뢰 상태(live)로 올린다.
    /// 일치하지 않으면 그대로 offlineSnapshot으로 남긴다.
    /// </summary>
    internal static async Task<IResult> AdoptOfflineAsync(
        AdoptSnapshotRequest request,
        IGraphStore graphStore,
        CancellationToken cancellationToken)
    {
        var graph = graphStore.Current;
        if (graph is null)
        {
            return ApiResults.NotAnalyzed();
        }

        if (graphStore.Origin != GraphOrigin.OfflineSnapshot)
        {
            return ApiResults.Error(
                StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, "가져온 분석 결과가 아니다.");
        }

        if (string.IsNullOrWhiteSpace(request.RootDirectory) || !Directory.Exists(request.RootDirectory))
        {
            return ApiResults.Error(
                StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, "연결할 원본 루트 디렉터리를 찾을 수 없다.");
        }

        var adopted = graph with { RootDirectory = Path.GetFullPath(request.RootDirectory) };
        var evaluator = new GraphFreshnessEvaluator();
        var freshness = await evaluator.EvaluateAsync(adopted, cancellationToken);
        if (freshness.Status != FreshnessStatus.Fresh)
        {
            return ApiResults.Error(
                StatusCodes.Status409Conflict,
                "INPUT_MISMATCH",
                "연결한 폴더의 소스가 이 분석과 다르다. 소스 접근을 열지 않는다.",
                new { changedFiles = freshness.ChangedFiles.Take(20).ToArray() });
        }

        graphStore.Set(adopted, GraphOrigin.Live, graphStore.OfflineGraphPath);
        return Results.Ok(new { origin = "live", rootDirectory = adopted.RootDirectory });
    }

    internal static AnalyzeResponse BuildSummary(AnalysisResult result) => new(
        result.Graph.EntryPoints.Count,
        result.Graph.Classes.Count,
        result.Graph.Classes.Count(item => item.TypeKind == ClassTypeKind.Interface),
        result.Graph.Nodes.Count,
        result.Graph.Edges.Count,
        result.MemberCount,
        result.GraphSizeBytes,
        result.ElapsedMs,
        result.Graph.AnalyzedAt,
        null,
        result.FindingCount)
    {
        SnapshotId = result.Graph.SnapshotId,
        InputFingerprint = result.Graph.InputFingerprint,
        CallSiteCount = result.Graph.CallSites.Count,
        ResolutionRate = result.Graph.Diagnostics?.Calls.ResolutionRate ?? 0,
        UnresolvedCallSites = (result.Graph.Diagnostics?.Calls.Ambiguous ?? 0) + (result.Graph.Diagnostics?.Calls.Unresolved ?? 0),
        MinimalApiCount = result.Graph.EntryPoints.Count(item => item.Source == EndpointSource.MinimalApi),
        Origin = "live",
        Notice = BuildNotice(result.Graph)
    };

    private static string? BuildNotice(GraphDocument graph)
    {
        var notes = new List<string>();
        if (graph.Diagnostics?.EntryPointNotes.Count > 0)
        {
            notes.AddRange(graph.Diagnostics.EntryPointNotes);
        }

        if (graph.Diagnostics?.ChangedDuringAnalysis == true)
        {
            notes.Add("분석 도중에 입력 파일이 바뀌었다. 다시 분석하는 편이 좋다.");
        }

        if (graph.Diagnostics?.ProjectsWithCompileErrors > 0)
        {
            notes.Add($"컴파일 오류가 있는 프로젝트가 {graph.Diagnostics.ProjectsWithCompileErrors}개다. 호출 관계가 빠졌을 수 있다.");
        }

        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    /// <summary>불러오기·가져오기가 공통으로 만드는 응답. 재분석과 달리 "이전 → 지금" 비교는 의미가 없어서 항상 비워 둔다
    /// (불러오기 전 메모리 상태가 다른 솔루션일 수도 있다).</summary>
    private static AnalyzeResponse BuildResponse(
        GraphDocument graph,
        int graphSizeBytes,
        long elapsedMs,
        IReadOnlyList<GraphValidationIssue> issues) => new(
        graph.EntryPoints.Count,
        graph.Classes.Count,
        graph.Classes.Count(item => item.TypeKind == ClassTypeKind.Interface),
        graph.Nodes.Count,
        graph.Edges.Count,
        graph.Classes.Sum(item => item.Members.Count),
        graphSizeBytes,
        elapsedMs,
        graph.AnalyzedAt,
        null,
        (graph.DataAccess?.Findings.Count ?? 0) + (graph.Middleware?.Findings.Count ?? 0))
    {
        SnapshotId = graph.SnapshotId,
        InputFingerprint = graph.InputFingerprint,
        CallSiteCount = graph.CallSites.Count,
        ResolutionRate = graph.Diagnostics?.Calls.ResolutionRate ?? 0,
        UnresolvedCallSites = (graph.Diagnostics?.Calls.Ambiguous ?? 0) + (graph.Diagnostics?.Calls.Unresolved ?? 0),
        MinimalApiCount = graph.EntryPoints.Count(item => item.Source == EndpointSource.MinimalApi),
        Origin = "live",
        Notice = issues.Count == 0 ? null : string.Join(" ", issues.Select(issue => issue.Message))
    };
}

public sealed record AnalyzeRequest(string SolutionPath);

public sealed record AdoptSnapshotRequest(string RootDirectory);

/// <param name="ClassCount">인터페이스를 포함한 전체 타입 수.</param>
/// <param name="MemberCount">클래스 다이어그램에 그릴 멤버 수(필드·프로퍼티·메서드·생성자).</param>
/// <param name="GraphSizeBytes">저장한 <c>graph.json</c>의 크기.</param>
public sealed record AnalyzeResponse(
    int EntryPointCount,
    int ClassCount,
    int InterfaceCount,
    int NodeCount,
    int EdgeCount,
    int MemberCount,
    int GraphSizeBytes,
    long ElapsedMs,
    DateTimeOffset AnalyzedAt,
    DateTimeOffset? PreviousAnalyzedAt,
    int FindingCount)
{
    public string SnapshotId { get; init; } = string.Empty;

    public string InputFingerprint { get; init; } = string.Empty;

    public int CallSiteCount { get; init; }

    /// <summary>해석한 지원 대상 호출 지점 / 검사한 지원 대상 호출 지점. 호출 "정확도"와는 다른 값이다.</summary>
    public double ResolutionRate { get; init; }

    public int UnresolvedCallSites { get; init; }

    public int MinimalApiCount { get; init; }

    public string Origin { get; init; } = "live";

    /// <summary>사용자에게 바로 알려야 할 안내(진입점 0개, 컴파일 오류 등).</summary>
    public string? Notice { get; init; }
}

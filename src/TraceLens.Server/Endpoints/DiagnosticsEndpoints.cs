using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;
using TraceLens.Context;

namespace TraceLens.Server.Endpoints;

public static class DiagnosticsEndpoints
{
    public static RouteGroupBuilder MapDiagnosticsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/diagnostics", GetAsync);
        group.MapGet("/analysis-status", GetStatusAsync);
        return group;
    }

    /// <summary>
    /// 환경과 현재 분석 능력을 돌려준다. 비밀값을 반환하지 않고 설치·설정 변경을 실행하지 않는다.
    /// </summary>
    internal static Task<IResult> GetAsync(
        string? target,
        IGraphStore store,
        EnvironmentDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var targetPath = string.IsNullOrWhiteSpace(target) ? store.Current?.SolutionPath : target;
        var report = diagnostics.Run(new DiagnosticsOptions
        {
            TargetPath = targetPath,
            Port = ServerHost.DefaultPort,
            McpHostCandidates = McpConfigWriter.GetHostCandidates()
        });
        // 500 오류에 붙일 것을 사용자가 알 수 있도록 로그 위치와 최근 오류를 함께 준다(비밀값은 담지 않는다).
        return Task.FromResult<IResult>(Results.Ok(report with
        {
            Log = new DiagnosticsLogInfo(ServerHost.LogFilePath, ServerHost.RecentErrors)
        }));
    }

    /// <summary>지금 무엇을 분석했고 얼마나 신선한지. MCP의 <c>get_analysis_status</c>와 같은 내용이다.</summary>
    internal static async Task<IResult> GetStatusAsync(
        IGraphStore store,
        IGraphFreshnessService freshnessService,
        CancellationToken cancellationToken)
    {
        var graph = store.Current;
        if (graph is null)
        {
            return ApiResults.NotAnalyzed();
        }

        var freshness = await freshnessService.CheckAsync(graph, cancellationToken);
        return Results.Ok(AnalysisStatus.From(graph, store.Origin, freshness));
    }
}

/// <param name="Origin">live면 이 기계에서 분석한 결과, offlineSnapshot이면 가져온 파일이다.</param>
public sealed record AnalysisStatus(
    string SnapshotId,
    string InputFingerprint,
    string SolutionPath,
    string RootDirectory,
    DateTimeOffset AnalyzedAt,
    int SchemaVersion,
    string AnalyzerVersion,
    string RulesVersion,
    string Origin,
    string Freshness,
    IReadOnlyList<string> FreshnessReasons,
    IReadOnlyList<string> ChangedFiles,
    AnalysisCapabilities Capabilities,
    AnalysisDiagnostics? Diagnostics)
{
    public static AnalysisStatus From(GraphDocument graph, GraphOrigin origin, FreshnessReport freshness) => new(
        graph.SnapshotId,
        graph.InputFingerprint,
        graph.SolutionPath,
        graph.RootDirectory,
        graph.AnalyzedAt,
        graph.SchemaVersion,
        string.IsNullOrEmpty(graph.AnalyzerVersion) ? TraceLensAnalyzer.AnalyzerVersion : graph.AnalyzerVersion,
        graph.RulesVersion,
        origin == GraphOrigin.Live ? "live" : "offlineSnapshot",
        freshness.Status.ToString().ToLowerInvariant(),
        freshness.Reasons,
        freshness.ChangedFiles.Take(50).ToArray(),
        new AnalysisCapabilities(
            graph.EntryPoints.Any(item => item.Source == EndpointSource.MinimalApi),
            graph.EntryPoints.Any(item => item.Source == EndpointSource.Controller),
            graph.CallSites.Count > 0,
            graph.Manifest is not null,
            graph.DataAccess is not null,
            graph.Middleware is not null,
            graph.StartupFlow is not null),
        graph.Diagnostics);
}

/// <summary>이 분석이 무엇을 담고 있는지. 도구가 "없는 기능"을 부르지 않도록 미리 알려준다.</summary>
public sealed record AnalysisCapabilities(
    bool MinimalApi,
    bool Controllers,
    bool CallSites,
    bool Manifest,
    bool DataAccess,
    bool Middleware,
    bool StartupFlow);

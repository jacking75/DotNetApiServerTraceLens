using System.Text.Json;
using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;

namespace TraceLens.Server.Endpoints;

public static class AnalyzeEndpoints
{
    public static RouteGroupBuilder MapAnalyzeEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/analyze", AnalyzeAsync);
        group.MapPost("/analyze/load", LoadSavedAsync);
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
            return Results.BadRequest(new { error = "solutionPath가 필요하다." });
        }

        try
        {
            var previousAnalyzedAt = graphStore.Current?.AnalyzedAt;
            var result = await analyzer.AnalyzeAsync(request.SolutionPath, cancellationToken);
            graphStore.Set(result.Graph);
            return Results.Ok(new AnalyzeResponse(
                result.Graph.EntryPoints.Count,
                result.Graph.Classes.Count,
                result.Graph.Classes.Count(item => item.TypeKind == ClassTypeKind.Interface),
                result.Graph.Nodes.Count,
                result.Graph.Edges.Count,
                result.MemberCount,
                result.GraphSizeBytes,
                result.ElapsedMs,
                result.Graph.AnalyzedAt,
                previousAnalyzedAt));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new { error = exception.Message });
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
            return Results.BadRequest(new { error = "solutionPath가 필요하다." });
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
            return Results.BadRequest(new { error = exception.Message });
        }

        if (!File.Exists(graphPath))
        {
            return Results.NotFound(new { error = "저장된 분석 결과가 없다. 먼저 분석해야 한다." });
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await using var stream = File.OpenRead(graphPath);
            var graph = await JsonSerializer.DeserializeAsync<GraphDocument>(stream, GraphDocument.JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("저장된 분석 결과를 읽을 수 없다.");
            stopwatch.Stop();
            graphStore.Set(graph);
            return Results.Ok(new AnalyzeResponse(
                graph.EntryPoints.Count,
                graph.Classes.Count,
                graph.Classes.Count(item => item.TypeKind == ClassTypeKind.Interface),
                graph.Nodes.Count,
                graph.Edges.Count,
                graph.Classes.Sum(item => item.Members.Count),
                (int)new FileInfo(graphPath).Length,
                stopwatch.ElapsedMilliseconds,
                graph.AnalyzedAt,
                // 재분석과 달리 "이전 → 지금" 비교는 의미가 없어서 항상 비워 둔다(불러오기 전 메모리 상태가 다른 솔루션일 수도 있다).
                null));
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            return Results.BadRequest(new { error = $"저장된 분석 결과가 손상됐다: {exception.Message}" });
        }
    }
}

public sealed record AnalyzeRequest(string SolutionPath);

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
    DateTimeOffset? PreviousAnalyzedAt);

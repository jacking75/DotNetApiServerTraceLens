using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;

namespace TraceLens.Server.Endpoints;

public static class AnalyzeEndpoints
{
    public static RouteGroupBuilder MapAnalyzeEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/analyze", AnalyzeAsync);
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

using TraceLens.Analyzer;

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
                result.Graph.Nodes.Count,
                result.Graph.Edges.Count,
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

public sealed record AnalyzeResponse(
    int EntryPointCount,
    int NodeCount,
    int EdgeCount,
    long ElapsedMs,
    DateTimeOffset AnalyzedAt,
    DateTimeOffset? PreviousAnalyzedAt);

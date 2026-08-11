using TraceLens.Analyzer;

namespace TraceLens.Server.Endpoints;

public static class AnalyzeEndpoints
{
    public static RouteGroupBuilder MapAnalyzeEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/analyze", AnalyzeAsync);
        return group;
    }

    private static async Task<IResult> AnalyzeAsync(
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
            var result = await analyzer.AnalyzeAsync(request.SolutionPath, cancellationToken);
            graphStore.Set(result.Graph);
            return Results.Ok(new
            {
                entryPointCount = result.Graph.EntryPoints.Count,
                nodeCount = result.Graph.Nodes.Count,
                edgeCount = result.Graph.Edges.Count,
                result.ElapsedMs
            });
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}

public sealed record AnalyzeRequest(string SolutionPath);

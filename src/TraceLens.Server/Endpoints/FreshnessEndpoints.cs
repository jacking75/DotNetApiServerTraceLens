namespace TraceLens.Server.Endpoints;

public static class FreshnessEndpoints
{
    public static RouteGroupBuilder MapFreshnessEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/freshness", GetFreshnessAsync);
        return group;
    }

    private static async Task<IResult> GetFreshnessAsync(
        IGraphStore graphStore,
        IGraphFreshnessService freshnessService,
        CancellationToken cancellationToken)
    {
        var graph = graphStore.Current;
        if (graph is null)
        {
            return Results.NotFound(new { error = "먼저 솔루션을 분석해야 한다." });
        }

        var freshness = await freshnessService.CheckAsync(graph, cancellationToken);
        return Results.Ok(new FreshnessResponse(
            freshness.IsFresh ? "fresh" : "stale",
            graph.AnalyzedAt,
            freshness.ChangedFiles));
    }
}

public sealed record FreshnessResponse(
    string Status,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<string> ChangedFiles);

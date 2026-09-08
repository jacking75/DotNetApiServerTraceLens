using TraceLens.Context;

namespace TraceLens.Server.Endpoints;

public static class FreshnessEndpoints
{
    public static RouteGroupBuilder MapFreshnessEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/freshness", GetFreshnessAsync);
        return group;
    }

    /// <summary>신선도 확인은 파일 목록·해시 비교일 뿐이며 재분석을 실행하지 않는다.</summary>
    private static async Task<IResult> GetFreshnessAsync(
        IGraphStore graphStore,
        IGraphFreshnessService freshnessService,
        CancellationToken cancellationToken)
    {
        var graph = graphStore.Current;
        if (graph is null)
        {
            return ApiResults.NotAnalyzed();
        }

        var freshness = await freshnessService.CheckAsync(graph, cancellationToken);
        return Results.Ok(new FreshnessResponse(
            freshness.Status.ToString().ToLowerInvariant(),
            graph.AnalyzedAt,
            freshness.ChangedFiles,
            graph.SnapshotId,
            freshness.Changes.Select(item => new FreshnessChangeResponse(item.Path, item.Kind.ToString())).ToArray(),
            freshness.Reasons,
            graphStore.Origin == GraphOrigin.Live ? "live" : "offlineSnapshot"));
    }
}

public sealed record FreshnessChangeResponse(string Path, string Kind);

/// <param name="Status">fresh · stale · unknown.</param>
public sealed record FreshnessResponse(
    string Status,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<string> ChangedFiles,
    string SnapshotId,
    IReadOnlyList<FreshnessChangeResponse> Changes,
    IReadOnlyList<string> Reasons,
    string Origin);

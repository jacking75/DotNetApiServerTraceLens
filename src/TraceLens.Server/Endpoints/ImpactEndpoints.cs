using TraceLens.Context;

namespace TraceLens.Server.Endpoints;

public static class ImpactEndpoints
{
    public static RouteGroupBuilder MapImpactEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/impact", AnalyzeAsync);
        return group;
    }

    /// <summary>
    /// 선택한 메서드를 바꾸면 영향을 받을 수 있는 HTTP 진입점을 찾는다.
    /// 결과는 호출 관계에서 도출한 <b>후보</b>이며 안전 판정이 아니다.
    /// </summary>
    internal static Task<IResult> AnalyzeAsync(
        ImpactApiRequest request,
        IGraphStore store,
        ImpactAnalyzer analyzer,
        CancellationToken cancellationToken)
    {
        var session = store.Session;
        if (session is null)
        {
            return Task.FromResult(ApiResults.NotAnalyzed());
        }

        if (request.NodeIds is not { Count: > 0 })
        {
            return Task.FromResult(ApiResults.Error(
                StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, "nodeIds가 최소 하나 필요하다."));
        }

        try
        {
            session.EnsureSnapshot(request.ExpectedSnapshotId);
        }
        catch (ContextException exception)
        {
            return Task.FromResult(ApiResults.FromContextException(exception));
        }

        var result = analyzer.Analyze(
            session.Index,
            new ImpactRequest(
                request.NodeIds,
                request.MaxDepth is > 0 and <= 64 ? request.MaxDepth.Value : 12,
                request.MaxNodes is > 0 and <= 20000 ? request.MaxNodes.Value : 4000,
                // 5초 고정이면 큰 솔루션에서 늘 잘린 결과만 본다. 상한은 60초다(T64).
                request.TimeoutMs is > 0 and <= 60000 ? request.TimeoutMs.Value : 5000),
            cancellationToken);
        return Task.FromResult(Results.Ok(result));
    }
}

public sealed record ImpactApiRequest(IReadOnlyList<string> NodeIds)
{
    public int? MaxDepth { get; init; }

    public int? MaxNodes { get; init; }

    /// <summary>시간 제한(ms). 비우면 5000, 상한은 60000이다.</summary>
    public int? TimeoutMs { get; init; }

    public string? ExpectedSnapshotId { get; init; }
}

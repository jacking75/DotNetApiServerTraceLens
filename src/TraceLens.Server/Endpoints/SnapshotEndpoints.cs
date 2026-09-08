using TraceLens.Context;

namespace TraceLens.Server.Endpoints;

public static class SnapshotEndpoints
{
    public static RouteGroupBuilder MapSnapshotEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/snapshots", ListAsync);
        group.MapPost("/snapshots", SaveAsync);
        group.MapPost("/snapshots/compare", CompareAsync);
        group.MapDelete("/snapshots/{snapshotId}", Delete);
        return group;
    }

    /// <summary>지금 분석을 "변경 전" 기준으로 보관한다. 저장 위치는 확인된 현재 대상의 <c>.tracelens/history</c>로 제한한다.</summary>
    internal static async Task<IResult> SaveAsync(
        SaveSnapshotRequest request,
        IGraphStore store,
        SnapshotStore snapshots,
        CancellationToken cancellationToken)
    {
        var graph = store.Current;
        if (graph is null)
        {
            return ApiResults.NotAnalyzed();
        }

        if (store.Origin == GraphOrigin.OfflineSnapshot)
        {
            return ApiResults.Error(
                StatusCodes.Status400BadRequest,
                "OFFLINE_SNAPSHOT",
                "가져온 분석 결과는 기준으로 저장하지 않는다. 이 기계에서 분석한 결과만 기준이 될 수 있다.");
        }

        try
        {
            var entry = await snapshots.SaveAsync(graph, request.Label ?? string.Empty, cancellationToken);
            var all = await snapshots.ListAsync(graph, cancellationToken);
            return Results.Ok(new SaveSnapshotResponse(
                entry,
                all.Count,
                all.Count >= SnapshotStore.SoftLimit
                    ? $"보관한 기준이 {all.Count}개다. 쓰지 않는 기준은 정리하는 편이 좋다."
                    : null));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, "SNAPSHOT_SAVE_FAILED", exception.Message);
        }
    }

    internal static async Task<IResult> ListAsync(
        IGraphStore store,
        SnapshotStore snapshots,
        CancellationToken cancellationToken)
    {
        var graph = store.Current;
        if (graph is null)
        {
            return ApiResults.NotAnalyzed();
        }

        var entries = await snapshots.ListAsync(graph, cancellationToken);
        return Results.Ok(new ListSnapshotsResponse(
            graph.SnapshotId,
            graph.AnalyzedAt,
            entries,
            SnapshotStore.SoftLimit));
    }

    /// <summary>보관한 기준과 지금 분석(또는 다른 기준)을 비교한다. 임의 경로는 받지 않는다.</summary>
    internal static async Task<IResult> CompareAsync(
        CompareSnapshotsRequest request,
        IGraphStore store,
        SnapshotStore snapshots,
        SnapshotComparer comparer,
        CancellationToken cancellationToken)
    {
        var current = store.Current;
        if (current is null)
        {
            return ApiResults.NotAnalyzed();
        }

        if (string.IsNullOrWhiteSpace(request.BaselineId))
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, "baselineId가 필요하다.");
        }

        var baseline = await snapshots.LoadAsync(current, request.BaselineId, cancellationToken);
        if (!baseline.Ok || baseline.Graph is null)
        {
            return ApiResults.Error(
                StatusCodes.Status404NotFound,
                "BASELINE_NOT_FOUND",
                $"기준 스냅샷을 읽을 수 없다: {request.BaselineId}",
                baseline.Issues);
        }

        var target = current;
        if (!string.IsNullOrWhiteSpace(request.CurrentId) &&
            !string.Equals(request.CurrentId, current.SnapshotId, StringComparison.Ordinal))
        {
            var stored = await snapshots.LoadAsync(current, request.CurrentId, cancellationToken);
            if (!stored.Ok || stored.Graph is null)
            {
                return ApiResults.Error(
                    StatusCodes.Status404NotFound,
                    "SNAPSHOT_NOT_FOUND",
                    $"비교 대상 스냅샷을 읽을 수 없다: {request.CurrentId}",
                    stored.Issues);
            }

            target = stored.Graph;
        }

        return Results.Ok(comparer.Compare(baseline.Graph, target));
    }

    internal static IResult Delete(string snapshotId, IGraphStore store, SnapshotStore snapshots)
    {
        var graph = store.Current;
        if (graph is null)
        {
            return ApiResults.NotAnalyzed();
        }

        try
        {
            return snapshots.Delete(graph, snapshotId)
                ? Results.Ok(new { deleted = snapshotId })
                : ApiResults.Error(StatusCodes.Status404NotFound, "SNAPSHOT_NOT_FOUND", $"보관한 기준이 없다: {snapshotId}");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, exception.Message);
        }
    }
}

public sealed record SaveSnapshotRequest(string? Label);

public sealed record SaveSnapshotResponse(SnapshotEntry Entry, int TotalCount, string? Notice);

public sealed record ListSnapshotsResponse(
    string CurrentSnapshotId,
    DateTimeOffset CurrentAnalyzedAt,
    IReadOnlyList<SnapshotEntry> Entries,
    int SoftLimit);

public sealed record CompareSnapshotsRequest(string BaselineId)
{
    /// <summary>비우면 지금 메모리에 있는 분석과 비교한다.</summary>
    public string? CurrentId { get; init; }
}

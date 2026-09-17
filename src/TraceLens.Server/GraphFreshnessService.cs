using TraceLens.Analyzer.Models;
using TraceLens.Context;

namespace TraceLens.Server;

public interface IGraphFreshnessService
{
    /// <summary>
    /// 현재 소스와 분석이 같은지 확인한다. 이 확인은 파일 목록·해시 비교일 뿐이며 재분석을 실행하지 않는다.
    /// 같은 스냅샷에 대한 동시 요청은 하나로 합쳐서 중복 해시를 막는다.
    /// </summary>
    Task<FreshnessReport> CheckAsync(GraphDocument graph, CancellationToken cancellationToken = default);
}

public sealed class GraphFreshnessService(GraphFreshnessEvaluator evaluator) : IGraphFreshnessService
{
    /// <summary>같은 스냅샷의 결과를 이 시간 동안 재사용한다. UI는 30초 주기로 부르므로 탭이 여럿일 때만 걸린다.</summary>
    private static readonly TimeSpan ResultTtl = TimeSpan.FromSeconds(5);

    private readonly object gate = new();
    private string? inFlightKey;
    private Task<FreshnessReport>? inFlight;
    private string? cachedKey;
    private FreshnessReport? cached;
    private DateTimeOffset cachedAt;

    public async Task<FreshnessReport> CheckAsync(GraphDocument graph, CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrEmpty(graph.SnapshotId)
            ? graph.AnalyzedAt.ToUnixTimeMilliseconds().ToString()
            : graph.SnapshotId;

        Task<FreshnessReport> shared;
        lock (gate)
        {
            if (cached is not null &&
                string.Equals(cachedKey, key, StringComparison.Ordinal) &&
                DateTimeOffset.UtcNow - cachedAt < ResultTtl)
            {
                return cached;
            }

            if (inFlight is { IsCompleted: false } && string.Equals(inFlightKey, key, StringComparison.Ordinal))
            {
                shared = inFlight;
            }
            else
            {
                inFlightKey = key;
                // 공유하는 평가는 호출자의 토큰으로 돌리지 않는다.
                // 첫 탭이 닫혔다고 해서 같은 평가를 기다리는 다른 탭이 취소되면 안 된다.
                shared = inFlight = evaluator.EvaluateAsync(graph, CancellationToken.None);
            }
        }

        var report = await shared.WaitAsync(cancellationToken);
        lock (gate)
        {
            cachedKey = key;
            cached = report;
            cachedAt = DateTimeOffset.UtcNow;
        }

        return report;
    }
}

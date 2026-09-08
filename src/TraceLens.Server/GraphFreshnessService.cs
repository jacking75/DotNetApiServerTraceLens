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
    private readonly object gate = new();
    private string? inFlightKey;
    private Task<FreshnessReport>? inFlight;

    public Task<FreshnessReport> CheckAsync(GraphDocument graph, CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrEmpty(graph.SnapshotId)
            ? graph.AnalyzedAt.ToUnixTimeMilliseconds().ToString()
            : graph.SnapshotId;

        lock (gate)
        {
            if (inFlight is { IsCompleted: false } && string.Equals(inFlightKey, key, StringComparison.Ordinal))
            {
                return inFlight;
            }

            inFlightKey = key;
            inFlight = evaluator.EvaluateAsync(graph, cancellationToken);
            return inFlight;
        }
    }
}

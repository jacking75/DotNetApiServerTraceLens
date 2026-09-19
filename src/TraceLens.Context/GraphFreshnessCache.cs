using System.Diagnostics;
using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

/// <param name="Report">신선도 결과.</param>
/// <param name="CheckedAt">이 결과를 실제로 계산한 시각. 캐시에서 온 값이면 과거다.</param>
/// <param name="DurationMs">계산에 걸린 시간. 캐시 적중이면 0이다.</param>
/// <param name="FromCache">파일을 다시 해시하지 않고 캐시에서 돌려줬는지.</param>
public sealed record FreshnessCheck(
    FreshnessReport Report,
    DateTimeOffset CheckedAt,
    long DurationMs,
    bool FromCache);

/// <summary>
/// 신선도 확인 결과를 짧게 재사용한다. Server와 MCP가 같은 것을 쓴다.
///
/// MCP는 도구마다 <c>new GraphFreshnessEvaluator()</c>를 만들어 매 호출이 솔루션 전체를 다시 해시했다.
/// 안내문이 "무엇을 묻든 get_analysis_status를 먼저"라고 시키는데 그 호출이 가장 비쌌다(T61).
///
/// 재분석은 하지 않는다 — 파일 목록·해시 비교만 한다(CLAUDE.md 제약 3).
/// </summary>
public sealed class GraphFreshnessCache(GraphFreshnessEvaluator evaluator, TimeSpan? ttl = null)
{
    /// <summary>같은 스냅샷의 결과를 이 시간 동안 재사용한다. UI는 30초 주기라 탭이 여럿일 때만 걸린다.</summary>
    private readonly TimeSpan resultTtl = ttl ?? TimeSpan.FromSeconds(5);

    private readonly object gate = new();
    private string? inFlightKey;
    private Task<FreshnessReport>? inFlight;
    private string? cachedKey;
    private FreshnessCheck? cached;

    /// <summary>해시를 실제로 계산한 횟수. 테스트가 "두 번째 호출은 다시 계산하지 않는다"를 확인한다.</summary>
    public int EvaluationCount { get; private set; }

    public async Task<FreshnessCheck> CheckAsync(GraphDocument graph, CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrEmpty(graph.SnapshotId)
            ? graph.AnalyzedAt.ToUnixTimeMilliseconds().ToString()
            : graph.SnapshotId;

        Task<FreshnessReport> shared;
        lock (gate)
        {
            if (cached is not null &&
                string.Equals(cachedKey, key, StringComparison.Ordinal) &&
                DateTimeOffset.UtcNow - cached.CheckedAt < resultTtl)
            {
                return cached with { FromCache = true, DurationMs = 0 };
            }

            if (inFlight is { IsCompleted: false } && string.Equals(inFlightKey, key, StringComparison.Ordinal))
            {
                shared = inFlight;
            }
            else
            {
                inFlightKey = key;
                EvaluationCount++;
                // 공유하는 평가는 호출자의 토큰으로 돌리지 않는다.
                // 첫 탭이 닫혔다고 해서 같은 평가를 기다리는 다른 탭이 취소되면 안 된다.
                shared = inFlight = evaluator.EvaluateAsync(graph, CancellationToken.None);
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var report = await shared.WaitAsync(cancellationToken);
        stopwatch.Stop();

        var result = new FreshnessCheck(report, DateTimeOffset.UtcNow, stopwatch.ElapsedMilliseconds, false);
        lock (gate)
        {
            cachedKey = key;
            cached = result;
        }

        return result;
    }
}

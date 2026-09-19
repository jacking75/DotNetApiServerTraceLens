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

/// <summary>
/// 캐시 자체는 <see cref="GraphFreshnessCache"/>(Context)에 있다. MCP도 같은 것을 쓴다 —
/// 예전에는 MCP가 도구마다 평가기를 새로 만들어 매 호출이 솔루션 전체를 다시 해시했다(T61).
/// </summary>
public sealed class GraphFreshnessService(GraphFreshnessEvaluator evaluator) : IGraphFreshnessService
{
    private readonly GraphFreshnessCache cache = new(evaluator);

    public async Task<FreshnessReport> CheckAsync(GraphDocument graph, CancellationToken cancellationToken = default) =>
        (await cache.CheckAsync(graph, cancellationToken)).Report;
}

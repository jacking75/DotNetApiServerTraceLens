using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 같은 <c>From/To</c> 쌍을 하나의 엣지로 합치면서 확실도를 정한다.
/// 한 쌍에 근거가 여럿이면 <b>가장 높은 확실도</b>를 따른다 — 람다 본문 안의 호출만 있으면 추정이고,
/// 직접 호출이 하나라도 있으면 확실이다.
/// </summary>
internal sealed class EdgeCollector
{
    private readonly Dictionary<(string From, string To), CallConfidence> edges = [];

    public void Add(string from, string to, CallConfidence confidence)
    {
        var key = (from, to);
        if (edges.TryGetValue(key, out var existing) && existing == CallConfidence.Certain)
        {
            return;
        }

        edges[key] = confidence;
    }

    /// <summary>
    /// 대상 노드가 그래프에 없어 버린 엣지 수.
    /// 해석률의 분자는 <b>실제로 엣지가 된 것</b>만 세야 한다 — 버린 것을 세면 분자가 부풀려진다(T58).
    /// </summary>
    public int DroppedForMissingNode { get; private set; }

    public IReadOnlyList<GraphEdge> Build(IReadOnlySet<string> knownNodeIds)
    {
        var kept = new List<GraphEdge>();
        var dropped = 0;
        foreach (var entry in edges)
        {
            if (knownNodeIds.Contains(entry.Key.From) && knownNodeIds.Contains(entry.Key.To))
            {
                kept.Add(new GraphEdge(entry.Key.From, entry.Key.To) { Confidence = entry.Value });
                continue;
            }

            dropped++;
        }

        DroppedForMissingNode = dropped;
        return kept
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }
}

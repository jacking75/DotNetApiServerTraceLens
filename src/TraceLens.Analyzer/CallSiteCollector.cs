using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 호출 지점의 근거와 통계를 모은다.
/// 엣지는 <c>From/To</c>로 중복 제거하지만 근거는 지점마다 남겨야 하므로 여기에 따로 쌓는다.
/// 같은 엣지에 호출이 아주 많은 경우(반복문 안 호출 등) <c>graph.json</c>이 커지지 않도록 상한을 둔다.
/// </summary>
internal sealed class CallSiteCollector(bool enabled, int maxPerEdge, int maxBoundary)
{
    private readonly List<CallSite> sites = [];
    private readonly Dictionary<string, int> perEdgeCount = new(StringComparer.Ordinal);
    private readonly HashSet<string> boundaryKeys = new(StringComparer.Ordinal);
    private int inspected;
    private int resolved;
    private int direct;
    private int diRegistration;
    private int singleImplementation;
    private int ambiguous;
    private int unresolved;
    private int external;
    private int droppedForLimit;

    public void Add(string fromNodeId, string? toNodeId, ResolvedCall call)
    {
        switch (call.Resolution)
        {
            case CallResolution.External:
                external++;
                return;
            case CallResolution.Direct:
                inspected++;
                resolved++;
                direct++;
                break;
            case CallResolution.DiRegistration:
                inspected++;
                resolved++;
                diRegistration++;
                break;
            case CallResolution.SingleImplementation:
                inspected++;
                resolved++;
                singleImplementation++;
                break;
            case CallResolution.Ambiguous:
                inspected++;
                ambiguous++;
                break;
            case CallResolution.Unresolved:
                inspected++;
                unresolved++;
                break;
        }

        if (!enabled)
        {
            return;
        }

        var target = toNodeId ?? string.Empty;
        if (target.Length == 0)
        {
            // 미해석·모호 호출은 "경계 목록"이라 파일·줄까지 같은 항목만 한 번 남긴다.
            if (boundaryKeys.Count >= maxBoundary)
            {
                droppedForLimit++;
                return;
            }

            if (!boundaryKeys.Add($"{fromNodeId}|{call.File}|{call.Line}|{call.DeclaredTarget}"))
            {
                return;
            }
        }
        else
        {
            var edgeKey = fromNodeId + "->" + target;
            perEdgeCount.TryGetValue(edgeKey, out var count);
            if (count >= maxPerEdge)
            {
                droppedForLimit++;
                return;
            }

            perEdgeCount[edgeKey] = count + 1;
        }

        sites.Add(new CallSite(
            fromNodeId,
            target,
            call.DeclaredTarget,
            call.File,
            call.Line,
            call.Resolution,
            call.Confidence)
        {
            Note = call.Note
        });
    }

    public IReadOnlyList<CallSite> Build(IReadOnlySet<string> knownNodeIds) => sites
        .Where(site => knownNodeIds.Contains(site.From) && (site.To.Length == 0 || knownNodeIds.Contains(site.To)))
        .OrderBy(site => site.From, StringComparer.Ordinal)
        .ThenBy(site => site.File, StringComparer.Ordinal)
        .ThenBy(site => site.Line)
        .ToArray();

    public CallResolutionStats Stats => new(
        inspected, resolved, direct, diRegistration, singleImplementation, ambiguous, unresolved, external);

    /// <summary>상한 때문에 목록에서 뺀 근거 수. 0이 아니면 제한을 화면·문맥에 알린다.</summary>
    public int DroppedForLimit => droppedForLimit;
}

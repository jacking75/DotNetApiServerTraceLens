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

    /// <summary>
    /// 솔루션 밖 호출을 네임스페이스로 센다. 개별 호출 지점은 저장하지 않는다(파일 크기).
    /// 키가 상한을 넘으면 새 네임스페이스는 "기타"로 합친다.
    /// </summary>
    private readonly Dictionary<string, (int Count, string SampleType)> externalByNamespace =
        new(StringComparer.Ordinal);

    /// <summary>집계할 네임스페이스 키의 상한. 넘으면 새 키를 만들지 않고 "기타"에 더한다.</summary>
    private const int MaxExternalNamespaceKeys = 200;

    /// <summary>화면·문맥에 내보내는 상위 네임스페이스 수.</summary>
    private const int TopExternalNamespaces = 20;

    private const string OtherNamespace = "(기타)";

    public void Add(string fromNodeId, string? toNodeId, ResolvedCall call)
    {
        switch (call.Resolution)
        {
            case CallResolution.External:
                external++;
                CountExternal(call);
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
            Note = call.Note,
            ViaLambda = call.ViaLambda
        });
    }

    public IReadOnlyList<CallSite> Build(IReadOnlySet<string> knownNodeIds) => sites
        .Where(site => knownNodeIds.Contains(site.From) && (site.To.Length == 0 || knownNodeIds.Contains(site.To)))
        .OrderBy(site => site.From, StringComparer.Ordinal)
        .ThenBy(site => site.File, StringComparer.Ordinal)
        .ThenBy(site => site.Line)
        .ToArray();

    /// <summary>상한을 넘어 <c>(기타)</c>로 합친 네임스페이스 종류. 잘렸다는 사실을 진단에 남긴다(T58).</summary>
    private readonly HashSet<string> truncatedNamespaces = new(StringComparer.Ordinal);

    private void CountExternal(ResolvedCall call)
    {
        var key = call.ExternalNamespace.Length > 0 ? call.ExternalNamespace : "(global)";
        if (!externalByNamespace.ContainsKey(key) && externalByNamespace.Count >= MaxExternalNamespaceKeys)
        {
            truncatedNamespaces.Add(key);
            key = OtherNamespace;
        }

        externalByNamespace.TryGetValue(key, out var entry);
        externalByNamespace[key] = (
            entry.Count + 1,
            entry.SampleType ?? (call.ExternalType.Length > 0 ? call.ExternalType : string.Empty));
    }

    public CallResolutionStats Stats => new(
        inspected, resolved, direct, diRegistration, singleImplementation, ambiguous, unresolved, external)
    {
        ExternalByNamespace = externalByNamespace
            .OrderByDescending(item => item.Value.Count)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(TopExternalNamespaces)
            .Select(item => new ExternalCallGroup(item.Key, item.Value.Count, item.Value.SampleType ?? string.Empty))
            .ToArray(),
        ExternalTruncatedKeys = truncatedNamespaces.Count
    };

    /// <summary>상한 때문에 목록에서 뺀 근거 수. 0이 아니면 제한을 화면·문맥에 알린다.</summary>
    public int DroppedForLimit => droppedForLimit;
}

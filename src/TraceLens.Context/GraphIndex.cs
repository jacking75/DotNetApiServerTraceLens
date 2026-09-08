using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

/// <summary>
/// 그래프 하나를 조회용으로 색인한다. 스냅샷은 불변이므로 <see cref="GraphDocument.SnapshotId"/>마다 한 번만 만들어 재사용한다.
/// Server와 MCP가 같은 결과를 돌려주도록 두 호스트가 이 색인을 공유한다.
/// </summary>
public sealed class GraphIndex
{
    /// <summary>엣지 키의 구분자. 노드 ID에 나올 수 없는 문자를 쓴다.</summary>
    private const string EdgeSeparator = "\u001f";

    private readonly Dictionary<string, GraphNode> nodeById;
    private readonly Dictionary<string, GraphClass> classById;
    private readonly Dictionary<string, List<string>> callersByNodeId;
    private readonly Dictionary<string, List<string>> calleesByNodeId;
    private readonly Dictionary<string, List<EntryPoint>> endpointsByNodeId;
    private readonly Dictionary<string, EntryPoint> endpointById;
    private readonly Dictionary<string, List<CallSite>> callSitesByEdge;
    private readonly Dictionary<string, List<CallSite>> boundaryByNodeId;
    private readonly Dictionary<string, GraphNode> nodeByStableKey;

    public GraphIndex(GraphDocument graph)
    {
        Graph = graph;
        nodeById = graph.Nodes.GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        classById = graph.Classes.GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        nodeByStableKey = graph.Nodes
            .Where(node => !string.IsNullOrEmpty(node.StableKey))
            .GroupBy(node => node.StableKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        callersByNodeId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        calleesByNodeId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            Add(calleesByNodeId, edge.From, edge.To);
            Add(callersByNodeId, edge.To, edge.From);
        }

        endpointsByNodeId = new Dictionary<string, List<EntryPoint>>(StringComparer.Ordinal);
        endpointById = new Dictionary<string, EntryPoint>(StringComparer.Ordinal);
        foreach (var entryPoint in graph.EntryPoints)
        {
            if (!endpointsByNodeId.TryGetValue(entryPoint.NodeId, out var list))
            {
                list = [];
                endpointsByNodeId[entryPoint.NodeId] = list;
            }

            list.Add(entryPoint);
            endpointById[EndpointKey(entryPoint)] = entryPoint;
        }

        callSitesByEdge = new Dictionary<string, List<CallSite>>(StringComparer.Ordinal);
        boundaryByNodeId = new Dictionary<string, List<CallSite>>(StringComparer.Ordinal);
        foreach (var site in graph.CallSites)
        {
            if (site.To.Length == 0)
            {
                if (!boundaryByNodeId.TryGetValue(site.From, out var boundary))
                {
                    boundary = [];
                    boundaryByNodeId[site.From] = boundary;
                }

                boundary.Add(site);
                continue;
            }

            var key = EdgeKey(site.From, site.To);
            if (!callSitesByEdge.TryGetValue(key, out var sites))
            {
                sites = [];
                callSitesByEdge[key] = sites;
            }

            sites.Add(site);
        }
    }

    public GraphDocument Graph { get; }

    public string SnapshotId => Graph.SnapshotId;

    public IReadOnlyCollection<GraphNode> Nodes => Graph.Nodes;

    public GraphNode? FindNode(string nodeId) =>
        nodeById.TryGetValue(nodeId, out var node) ? node : null;

    public GraphNode? FindNodeByStableKey(string stableKey) =>
        nodeByStableKey.TryGetValue(stableKey, out var node) ? node : null;

    public GraphClass? FindClass(string classId) =>
        classById.TryGetValue(classId, out var item) ? item : null;

    public IReadOnlyList<string> GetCallers(string nodeId) =>
        callersByNodeId.TryGetValue(nodeId, out var list) ? list : [];

    public IReadOnlyList<string> GetCallees(string nodeId) =>
        calleesByNodeId.TryGetValue(nodeId, out var list) ? list : [];

    public IReadOnlyList<EntryPoint> GetEndpointsForNode(string nodeId) =>
        endpointsByNodeId.TryGetValue(nodeId, out var list) ? list : [];

    /// <summary>
    /// 엔드포인트 ID로 찾는다. v6 이하 그래프에는 <c>endpointId</c>가 없으므로
    /// <c>METHOD 라우트</c>와 노드 ID로도 호환 조회한다(유실된 복수 라우트를 복원하는 것은 아니다).
    /// </summary>
    public EntryPoint? FindEndpoint(string endpointId)
    {
        if (endpointById.TryGetValue(endpointId, out var endpoint))
        {
            return endpoint;
        }

        return Graph.EntryPoints.FirstOrDefault(item =>
            string.Equals(item.EndpointId, endpointId, StringComparison.Ordinal) ||
            string.Equals($"{item.HttpMethod} {item.Route}", endpointId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.NodeId, endpointId, StringComparison.Ordinal));
    }

    /// <summary>이 엣지가 생긴 근거(실제 호출 줄).</summary>
    public IReadOnlyList<CallSite> GetCallSites(string from, string to) =>
        callSitesByEdge.TryGetValue(EdgeKey(from, to), out var sites) ? sites : [];

    /// <summary>확정하지 못한 호출(미해석·모호). 그래프의 "빈 부분"을 호출 없음으로 오해하지 않게 한다.</summary>
    public IReadOnlyList<CallSite> GetBoundaries(string nodeId) =>
        boundaryByNodeId.TryGetValue(nodeId, out var sites) ? sites : [];

    /// <summary>v6 이하 그래프에도 쓸 수 있는 엔드포인트 키.</summary>
    public static string EndpointKey(EntryPoint entryPoint) =>
        string.IsNullOrEmpty(entryPoint.EndpointId)
            ? $"{entryPoint.HttpMethod} {entryPoint.Route} :: {entryPoint.NodeId}"
            : entryPoint.EndpointId;

    private static string EdgeKey(string from, string to) => from + EdgeSeparator + to;

    private static void Add(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }
}

using System.ComponentModel;
using ModelContextProtocol.Server;
using TraceLens.Analyzer.Models;

namespace TraceLens.Mcp;

[McpServerToolType]
public sealed class TraceLensTools(GraphFileStore store)
{
    [McpServerTool(Name = "get_node")]
    [Description("노드 ID로 메타데이터와 해당 메서드의 소스를 조회한다.")]
    public async Task<NodeResult> GetNodeAsync(
        [Description("TraceLens 그래프 노드 ID")] string nodeId,
        CancellationToken cancellationToken)
    {
        var graph = await store.LoadAsync(cancellationToken);
        var node = FindNode(graph, nodeId);
        var source = await store.ReadSourceAsync(
            graph,
            node.File,
            node.StartLine,
            node.EndLine,
            cancellationToken);
        return new NodeResult(node, source);
    }

    [McpServerTool(Name = "get_source")]
    [Description("분석 대상 솔루션 내부 파일의 지정된 라인 범위를 읽는다.")]
    public async Task<SourceResult> GetSourceAsync(
        [Description("솔루션 루트 기준 상대 파일 경로")] string file,
        [Description("1부터 시작하는 시작 라인")] int startLine,
        [Description("1부터 시작하는 끝 라인")] int endLine,
        CancellationToken cancellationToken)
    {
        var graph = await store.LoadAsync(cancellationToken);
        return await store.ReadSourceAsync(graph, file, startLine, endLine, cancellationToken);
    }

    [McpServerTool(Name = "get_callers")]
    [Description("지정한 노드를 직접 호출하는 인접 노드를 조회한다.")]
    public async Task<IReadOnlyList<GraphNode>> GetCallersAsync(
        [Description("TraceLens 그래프 노드 ID")] string nodeId,
        CancellationToken cancellationToken)
    {
        var graph = await store.LoadAsync(cancellationToken);
        _ = FindNode(graph, nodeId);
        var callerIds = graph.Edges.Where(edge => edge.To == nodeId).Select(edge => edge.From).ToHashSet();
        return graph.Nodes.Where(node => callerIds.Contains(node.Id)).ToArray();
    }

    [McpServerTool(Name = "get_callees")]
    [Description("지정한 노드가 직접 호출하는 인접 노드를 조회한다.")]
    public async Task<IReadOnlyList<GraphNode>> GetCalleesAsync(
        [Description("TraceLens 그래프 노드 ID")] string nodeId,
        CancellationToken cancellationToken)
    {
        var graph = await store.LoadAsync(cancellationToken);
        _ = FindNode(graph, nodeId);
        var calleeIds = graph.Edges.Where(edge => edge.From == nodeId).Select(edge => edge.To).ToHashSet();
        return graph.Nodes.Where(node => calleeIds.Contains(node.Id)).ToArray();
    }

    [McpServerTool(Name = "get_entrypoints")]
    [Description("현재 TraceLens 그래프의 HTTP 진입점 목록을 조회한다.")]
    public async Task<IReadOnlyList<EntryPoint>> GetEntryPointsAsync(CancellationToken cancellationToken)
    {
        var graph = await store.LoadAsync(cancellationToken);
        return graph.EntryPoints;
    }

    [McpServerTool(Name = "search_symbol")]
    [Description("표시 이름, 전체 노드 ID 또는 네임스페이스에 포함된 문자열로 노드를 검색한다.")]
    public async Task<IReadOnlyList<GraphNode>> SearchSymbolAsync(
        [Description("대소문자를 구분하지 않는 검색 문자열")] string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("query가 필요하다.", nameof(query));
        }

        var graph = await store.LoadAsync(cancellationToken);
        return graph.Nodes
            .Where(node => node.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           node.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           node.Namespace.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static GraphNode FindNode(GraphDocument graph, string nodeId) =>
        graph.Nodes.FirstOrDefault(node => node.Id == nodeId)
        ?? throw new KeyNotFoundException($"노드를 찾을 수 없다: {nodeId}");
}

public sealed record NodeResult(GraphNode Node, SourceResult Source);

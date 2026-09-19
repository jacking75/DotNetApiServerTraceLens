using TraceLens.Analyzer.Models;

namespace TraceLens.Server.Endpoints;

public static class GraphEndpoints
{
    public static RouteGroupBuilder MapGraphEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/graph/entrypoints", (IGraphStore store) =>
            store.Current is { } graph
                ? Results.Ok(graph.EntryPoints)
                : ApiResults.NotAnalyzed());
        group.MapGet("/graph/namespaces", (IGraphStore store) =>
            store.Current is { } graph
                ? Results.Ok(GraphNamespaceSummary.From(graph))
                : ApiResults.NotAnalyzed());
        group.MapGet("/graph", (IGraphStore store) =>
            store.Current is { } graph
                ? Results.Ok(graph)
                : ApiResults.NotAnalyzed());
        return group;
    }
}

/// <summary>
/// 클래스 다이어그램의 좌측 네임스페이스 패널용 요약. 전체 그래프를 받기 전에도 목록을 그릴 수 있게 한다.
/// </summary>
/// <param name="Kinds">그 네임스페이스에 있는 클래스 분류 목록. 목록의 색 점에 쓴다.</param>
public sealed record GraphNamespaceSummary(
    string Namespace,
    int ClassCount,
    int MethodCount,
    IReadOnlyList<NodeKind> Kinds)
{
    public static IReadOnlyList<GraphNamespaceSummary> From(GraphDocument graph) => graph.Classes
        .GroupBy(item => item.Namespace, StringComparer.Ordinal)
        .Select(group => new GraphNamespaceSummary(
            group.Key,
            group.Count(),
            group.Sum(item => item.MethodIds.Count),
            group.Select(item => item.Kind).Distinct().OrderBy(kind => kind).ToArray()))
        .OrderBy(item => item.Namespace, StringComparer.Ordinal)
        .ToArray();
}

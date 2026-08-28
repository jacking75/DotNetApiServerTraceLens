using TraceLens.Analyzer.Models;

namespace TraceLens.Server.Endpoints;

public static class GraphEndpoints
{
    public static RouteGroupBuilder MapGraphEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/graph/entrypoints", (IGraphStore store) =>
            store.Current is { } graph
                ? Results.Ok(graph.EntryPoints)
                : Results.NotFound(new { error = "먼저 솔루션을 분석해야 한다." }));
        group.MapGet("/graph/classes", (IGraphStore store) =>
            store.Current is { } graph
                ? Results.Ok(graph.Classes.Select(GraphClassSummary.From).ToArray())
                : Results.NotFound(new { error = "먼저 솔루션을 분석해야 한다." }));
        group.MapGet("/graph", (IGraphStore store) =>
            store.Current is { } graph
                ? Results.Ok(graph)
                : Results.NotFound(new { error = "먼저 솔루션을 분석해야 한다." }));
        return group;
    }
}

/// <summary>클래스 목록 메뉴에 필요한 최소 정보만 담는다(메서드 ID 목록은 /api/graph에서 받는다).</summary>
public sealed record GraphClassSummary(
    string Id,
    string Name,
    string Namespace,
    NodeKind Kind,
    string File,
    int StartLine,
    int EndLine,
    int MethodCount)
{
    public static GraphClassSummary From(GraphClass graphClass) => new(
        graphClass.Id,
        graphClass.Name,
        graphClass.Namespace,
        graphClass.Kind,
        graphClass.File,
        graphClass.StartLine,
        graphClass.EndLine,
        graphClass.MethodIds.Count);
}

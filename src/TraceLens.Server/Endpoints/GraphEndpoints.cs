namespace TraceLens.Server.Endpoints;

public static class GraphEndpoints
{
    public static RouteGroupBuilder MapGraphEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/graph/entrypoints", (IGraphStore store) =>
            store.Current is { } graph
                ? Results.Ok(graph.EntryPoints)
                : Results.NotFound(new { error = "먼저 솔루션을 분석해야 한다." }));
        group.MapGet("/graph", (IGraphStore store) =>
            store.Current is { } graph
                ? Results.Ok(graph)
                : Results.NotFound(new { error = "먼저 솔루션을 분석해야 한다." }));
        return group;
    }
}

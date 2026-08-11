using TraceLens.Analyzer.Models;

namespace TraceLens.Server;

public interface IGraphStore
{
    GraphDocument? Current { get; }
    void Set(GraphDocument graph);
}

public sealed class GraphStore : IGraphStore
{
    private GraphDocument? current;

    public GraphDocument? Current => Volatile.Read(ref current);

    public void Set(GraphDocument graph) => Volatile.Write(ref current, graph);
}

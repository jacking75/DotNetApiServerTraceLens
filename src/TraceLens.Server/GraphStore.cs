using TraceLens.Analyzer.Models;
using TraceLens.Context;

namespace TraceLens.Server;

/// <summary>지금 보고 있는 분석 결과가 어디서 왔는지.</summary>
public enum GraphOrigin
{
    /// <summary>이 기계에서 분석했거나, 대상 폴더의 <c>.tracelens/graph.json</c>을 그대로 읽었다.</summary>
    Live,

    /// <summary>사용자가 고른 파일에서 가져왔다. 원본 루트가 확인되기 전에는 로컬 소스를 읽지 않는다.</summary>
    OfflineSnapshot
}

public interface IGraphStore
{
    GraphDocument? Current { get; }

    GraphOrigin Origin { get; }

    /// <summary>가져온 그래프를 로컬에 보관한 경로. MCP에 전달할 때 이 경로를 쓴다.</summary>
    string? OfflineGraphPath { get; }

    /// <summary>한 요청 안에서 고정해 쓰는 조회 세션. 그래프가 바뀌어도 진행 중인 요청은 같은 스냅샷을 본다.</summary>
    SnapshotSession? Session { get; }

    void Set(GraphDocument graph, GraphOrigin origin = GraphOrigin.Live, string? offlineGraphPath = null);
}

public sealed class GraphStore : IGraphStore
{
    private sealed record State(GraphDocument Graph, GraphOrigin Origin, string? OfflineGraphPath, SnapshotSession Session);

    private State? state;

    public GraphDocument? Current => Volatile.Read(ref state)?.Graph;

    public GraphOrigin Origin => Volatile.Read(ref state)?.Origin ?? GraphOrigin.Live;

    public string? OfflineGraphPath => Volatile.Read(ref state)?.OfflineGraphPath;

    public SnapshotSession? Session => Volatile.Read(ref state)?.Session;

    public void Set(GraphDocument graph, GraphOrigin origin = GraphOrigin.Live, string? offlineGraphPath = null)
    {
        var index = new GraphIndex(graph);
        // 가져온 그래프는 rootDirectory만 보고 로컬 파일 접근 권한을 주지 않는다.
        var policy = SourceAccessPolicy.ForGraph(graph, trusted: origin == GraphOrigin.Live);
        var session = new SnapshotSession(index, new SourceReader(graph, policy));
        Volatile.Write(ref state, new State(graph, origin, offlineGraphPath, session));
    }
}

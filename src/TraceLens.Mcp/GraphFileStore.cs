using TraceLens.Analyzer.Models;
using TraceLens.Context;

namespace TraceLens.Mcp;

/// <summary>
/// 디스크의 <c>graph.json</c>을 읽어 스냅샷 세션을 만든다.
/// 한 번의 도구 호출은 고정한 스냅샷을 쓰고, 같은 파일이면 색인을 재사용해서 전체 파일 읽기를 반복하지 않는다.
/// 갱신 감지는 파일 메타데이터를 먼저 보되, 세대는 실제로 읽어 검증한 뒤 <c>snapshotId</c>로 확정한다.
/// </summary>
public sealed class GraphFileStore(string graphPath)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CachedSnapshot? cached;

    public string GraphPath { get; } = Path.GetFullPath(graphPath);

    private sealed record CachedSnapshot(
        SnapshotSession Session,
        GraphDocument Graph,
        DateTime LastWriteUtc,
        long Length,
        IReadOnlyList<GraphValidationIssue> Issues);

    public async Task<SnapshotSession> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetAsync(cancellationToken);
        return snapshot.Session;
    }

    public async Task<GraphDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetAsync(cancellationToken);
        return snapshot.Graph;
    }

    public async Task<IReadOnlyList<GraphValidationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetAsync(cancellationToken);
        return snapshot.Issues;
    }

    private async Task<CachedSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(GraphPath))
        {
            throw new FileNotFoundException("TraceLens 그래프 파일을 찾을 수 없다.", GraphPath);
        }

        var info = new FileInfo(GraphPath);
        var current = Volatile.Read(ref cached);
        if (current is not null && current.LastWriteUtc == info.LastWriteTimeUtc && current.Length == info.Length)
        {
            return current;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            info.Refresh();
            current = Volatile.Read(ref cached);
            if (current is not null && current.LastWriteUtc == info.LastWriteTimeUtc && current.Length == info.Length)
            {
                return current;
            }

            var loaded = await GraphJsonStore.LoadAsync(GraphPath, cancellationToken);
            if (!loaded.Ok || loaded.Graph is null)
            {
                throw new InvalidDataException(
                    "TraceLens 그래프 파일을 읽을 수 없다: " + string.Join(", ", loaded.Issues.Select(issue => issue.Message)));
            }

            var graph = loaded.Graph;
            var session = new SnapshotSession(
                new GraphIndex(graph),
                new SourceReader(graph, SourceAccessPolicy.ForGraph(graph)));
            var snapshot = new CachedSnapshot(session, graph, info.LastWriteTimeUtc, info.Length, loaded.Issues);
            Volatile.Write(ref cached, snapshot);
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }
}

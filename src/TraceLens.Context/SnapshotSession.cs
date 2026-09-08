using Microsoft.Extensions.DependencyInjection;

namespace TraceLens.Context;

/// <summary>
/// 스냅샷 하나에 대한 조회 세션이다.
/// 한 번의 요청 안에서 그래프·색인·소스 접근 정책을 고정해서, 조회 도중에 분석이 바뀌어도 결과가 섞이지 않게 한다.
/// </summary>
public sealed record SnapshotSession(GraphIndex Index, SourceReader Source)
{
    public string SnapshotId => Index.SnapshotId;

    public void EnsureSnapshot(string? expectedSnapshotId)
    {
        if (!string.IsNullOrEmpty(expectedSnapshotId) &&
            !string.Equals(expectedSnapshotId, SnapshotId, StringComparison.Ordinal))
        {
            throw new ContextException(
                "SNAPSHOT_MISMATCH",
                $"기대한 스냅샷({expectedSnapshotId})과 현재 분석({SnapshotId})이 다르다. 다시 조회해야 한다.");
        }
    }
}

public static class ContextServiceCollectionExtensions
{
    public static IServiceCollection AddTraceLensContext(this IServiceCollection services) => services
        .AddSingleton<GraphFreshnessEvaluator>()
        .AddSingleton<ContextBuilder>()
        .AddSingleton<ImpactAnalyzer>()
        .AddSingleton<SnapshotComparer>()
        .AddSingleton<SnapshotStore>()
        .AddSingleton<ShareReportBuilder>()
        .AddSingleton<EnvironmentDiagnostics>();
}

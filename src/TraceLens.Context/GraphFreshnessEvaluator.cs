using System.Collections.Concurrent;
using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

/// <summary>분석 결과와 현재 소스가 같은지.</summary>
public enum FreshnessStatus
{
    /// <summary>기록한 입력과 지금 파일이 모두 같다.</summary>
    Fresh,

    /// <summary>바뀐 입력이 있다. 다시 분석해야 한다.</summary>
    Stale,

    /// <summary>확인 범위를 벗어났다(분석 루트를 찾을 수 없거나 신선도 입력이 없는 옛 그래프).</summary>
    Unknown
}

public enum FreshnessChangeKind
{
    Modified,
    Deleted,
    Added,
    ConfigChanged,
    Unreadable
}

public sealed record FreshnessChange(string Path, FreshnessChangeKind Kind);

public sealed record FreshnessReport(
    FreshnessStatus Status,
    DateTimeOffset AnalyzedAt,
    string SnapshotId,
    IReadOnlyList<FreshnessChange> Changes,
    IReadOnlyList<string> Reasons)
{
    public IReadOnlyList<string> ChangedFiles => Changes.Select(item => item.Path).ToArray();
}

/// <summary>
/// 기록한 파일 해시뿐 아니라 <b>소스 파일 집합</b>도 비교해서 새로 생긴 <c>.cs</c>와 설정 변경까지 잡는다.
/// 이 검사는 파일 목록·해시 비교일 뿐이며 Roslyn 재분석을 실행하지 않는다.
/// </summary>
public sealed class GraphFreshnessEvaluator
{
    /// <summary>솔루션 전체를 분석하면 해시 대상 파일이 많아지므로 제한된 병렬로 읽는다.</summary>
    private const int MaxParallelHashing = 8;

    public async Task<FreshnessReport> EvaluateAsync(
        GraphDocument graph,
        CancellationToken cancellationToken = default)
    {
        var rootDirectory = graph.RootDirectory;
        if (string.IsNullOrEmpty(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return new FreshnessReport(
                FreshnessStatus.Unknown,
                graph.AnalyzedAt,
                graph.SnapshotId,
                [],
                ["분석 루트 디렉터리를 찾을 수 없어 현재 소스와 비교하지 못했다."]);
        }

        var changes = new ConcurrentBag<FreshnessChange>();
        var reasons = new List<string>();

        await Parallel.ForEachAsync(
            graph.FileHashes,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelHashing, CancellationToken = cancellationToken },
            async (entry, token) =>
            {
                var (relativePath, expectedHash) = entry;
                var fullPath = Path.Combine(rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    changes.Add(new FreshnessChange(relativePath, FreshnessChangeKind.Deleted));
                    return;
                }

                var currentHash = await SnapshotHasher.ComputeFileAsync(fullPath, token);
                if (currentHash == SnapshotHasher.UnknownHash)
                {
                    changes.Add(new FreshnessChange(relativePath, FreshnessChangeKind.Unreadable));
                    return;
                }

                if (!string.Equals(currentHash, expectedHash, StringComparison.Ordinal))
                {
                    changes.Add(new FreshnessChange(relativePath, FreshnessChangeKind.Modified));
                }
            });

        var manifest = graph.Manifest;
        if (manifest is null)
        {
            reasons.Add("이 그래프에는 분석 입력 목록(manifest)이 없다. 새 파일과 설정 변경은 확인하지 못한다.");
            var legacyStatus = changes.IsEmpty ? FreshnessStatus.Unknown : FreshnessStatus.Stale;
            return new FreshnessReport(
                legacyStatus,
                graph.AnalyzedAt,
                graph.SnapshotId,
                Order(changes),
                legacyStatus == FreshnessStatus.Unknown
                    ? [.. reasons, "다시 분석하면 새 파일·설정 변경까지 확인할 수 있다."]
                    : reasons);
        }

        foreach (var input in manifest.Inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(rootDirectory, input.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                changes.Add(new FreshnessChange(input.Path, FreshnessChangeKind.Deleted));
                continue;
            }

            var currentHash = await SnapshotHasher.ComputeFileAsync(fullPath, cancellationToken);
            if (!string.Equals(currentHash, input.Hash, StringComparison.Ordinal))
            {
                changes.Add(new FreshnessChange(input.Path, FreshnessChangeKind.ConfigChanged));
            }
        }

        // 새로 생긴 소스 파일은 해시 목록에 없으므로 파일 집합 비교로 찾는다.
        var known = graph.FileHashes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in AnalysisManifestBuilder.EnumerateSourceFiles(rootDirectory, manifest))
        {
            if (!known.Contains(file))
            {
                changes.Add(new FreshnessChange(file, FreshnessChangeKind.Added));
            }
        }

        if (manifest.LinkedSources.Count > 0)
        {
            reasons.Add($"분석 루트 밖에서 링크된 소스 {manifest.LinkedSources.Count}개는 새 파일 검사 대상이 아니다.");
        }

        reasons.AddRange(manifest.Limitations);
        var ordered = Order(changes);
        var status = ordered.Count == 0 ? FreshnessStatus.Fresh : FreshnessStatus.Stale;
        return new FreshnessReport(status, graph.AnalyzedAt, graph.SnapshotId, ordered, reasons);
    }

    private static IReadOnlyList<FreshnessChange> Order(IEnumerable<FreshnessChange> changes) => changes
        .DistinctBy(item => item.Path + "|" + item.Kind, StringComparer.Ordinal)
        .OrderBy(item => item.Kind)
        .ThenBy(item => item.Path, StringComparer.Ordinal)
        .ToArray();
}

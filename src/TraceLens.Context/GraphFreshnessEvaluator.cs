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
///
/// UI가 30초마다 부르므로 두 번째 호출부터는 <b>길이·수정 시각이 같은 파일의 해시를 다시 계산하지 않는다</b>.
/// 파일 감시(FileSystemWatcher)는 쓰지 않는다 — 자동 재분석과 혼동되지 않게 하려는 설계 제약이다(CLAUDE.md 3).
/// </summary>
// 테스트에서 느린 평가를 흉내 낼 수 있도록 sealed를 풀고 EvaluateAsync를 virtual로 둔다.
public class GraphFreshnessEvaluator
{
    /// <summary>솔루션 전체를 분석하면 해시 대상 파일이 많아지므로 제한된 병렬로 읽는다.</summary>
    private const int MaxParallelHashing = 8;

    /// <summary>디렉터리 열거 결과를 재사용하는 시간. 이 안에서는 같은 목록을 그대로 쓴다.</summary>
    private static readonly TimeSpan ListingCacheTtl = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, CachedHash> hashCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedListing> listingCache = new(StringComparer.OrdinalIgnoreCase);
    private int hashComputations;

    /// <summary>실제로 SHA-256을 계산한 횟수. 캐시가 도는지 테스트에서 확인한다.</summary>
    public int HashComputations => Volatile.Read(ref hashComputations);

    private sealed record CachedHash(long Length, DateTime LastWriteUtc, string Hash);

    private sealed record CachedListing(DateTimeOffset At, IReadOnlyList<string> Files);

    /// <summary>
    /// 파일 해시를 돌려준다. 길이와 수정 시각이 지난번과 같으면 파일을 다시 읽지 않는다.
    /// 파일이 없으면 null이다.
    /// </summary>
    private async Task<string?> ComputeHashAsync(string fullPath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            hashCache.TryRemove(fullPath, out _);
            return null;
        }

        if (hashCache.TryGetValue(fullPath, out var cached) &&
            cached.Length == info.Length &&
            cached.LastWriteUtc == info.LastWriteTimeUtc)
        {
            return cached.Hash;
        }

        Interlocked.Increment(ref hashComputations);
        var hash = await SnapshotHasher.ComputeFileAsync(fullPath, cancellationToken);
        if (hash != SnapshotHasher.UnknownHash)
        {
            hashCache[fullPath] = new CachedHash(info.Length, info.LastWriteTimeUtc, hash);
        }

        return hash;
    }

    /// <summary>소스 루트 아래 <c>.cs</c> 목록. 짧은 TTL 동안 재사용한다(30초 주기 호출이 매번 전체 열거를 하지 않게).</summary>
    private IReadOnlyList<string> EnumerateSourceFilesCached(string rootDirectory, AnalysisManifest manifest)
    {
        var key = rootDirectory + "|" + string.Join('|', manifest.SourceRoots);
        if (listingCache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < ListingCacheTtl)
        {
            return cached.Files;
        }

        var files = AnalysisManifestBuilder.EnumerateSourceFiles(rootDirectory, manifest);
        listingCache[key] = new CachedListing(DateTimeOffset.UtcNow, files);
        return files;
    }

    public virtual async Task<FreshnessReport> EvaluateAsync(
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
                var currentHash = await ComputeHashAsync(fullPath, token);
                if (currentHash is null)
                {
                    changes.Add(new FreshnessChange(relativePath, FreshnessChangeKind.Deleted));
                    return;
                }

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
            var currentHash = await ComputeHashAsync(fullPath, cancellationToken);
            if (currentHash is null)
            {
                changes.Add(new FreshnessChange(input.Path, FreshnessChangeKind.Deleted));
                continue;
            }

            if (!string.Equals(currentHash, input.Hash, StringComparison.Ordinal))
            {
                changes.Add(new FreshnessChange(input.Path, FreshnessChangeKind.ConfigChanged));
            }
        }

        // 새로 생긴 소스 파일은 해시 목록에 없으므로 파일 집합 비교로 찾는다.
        // 분석 당시에도 컴파일되지 않던 파일(<Compile Remove> 등)은 "새 파일"이 아니다.
        var known = graph.FileHashes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ignored = manifest.IgnoredSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateSourceFilesCached(rootDirectory, manifest))
        {
            if (!known.Contains(file) && !ignored.Contains(file))
            {
                changes.Add(new FreshnessChange(file, FreshnessChangeKind.Added));
            }
        }

        if (ignored.Count > 0)
        {
            reasons.Add(
                $"어떤 프로젝트도 컴파일하지 않는 .cs {ignored.Count}개는 신선도 검사에서 뺀다" +
                "(<Compile Remove>·조건부 컴파일). 그 파일이 바뀌어도 stale로 보지 않는다.");
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

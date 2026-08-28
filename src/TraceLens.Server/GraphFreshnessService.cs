using System.Collections.Concurrent;
using System.Security.Cryptography;
using TraceLens.Analyzer.Models;

namespace TraceLens.Server;

public interface IGraphFreshnessService
{
    Task<GraphFreshnessResult> CheckAsync(GraphDocument graph, CancellationToken cancellationToken = default);
}

public sealed class GraphFreshnessService : IGraphFreshnessService
{
    /// <summary>솔루션 전체를 분석하면 해시 대상 파일이 많아지므로 제한된 병렬로 읽는다.</summary>
    private const int MaxParallelHashing = 8;

    public async Task<GraphFreshnessResult> CheckAsync(
        GraphDocument graph,
        CancellationToken cancellationToken = default)
    {
        var solutionDirectory = graph.RootDirectory;
        if (string.IsNullOrEmpty(solutionDirectory))
        {
            throw new InvalidOperationException("분석 루트 디렉터리를 확인할 수 없다.");
        }

        var changedFiles = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(
            graph.FileHashes,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelHashing, CancellationToken = cancellationToken },
            async (entry, token) =>
            {
                var (relativePath, expectedHash) = entry;
                var fullPath = Path.Combine(solutionDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    await using var stream = File.OpenRead(fullPath);
                    var hash = await SHA256.HashDataAsync(stream, token);
                    var currentHash = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
                    if (!string.Equals(currentHash, expectedHash, StringComparison.Ordinal))
                    {
                        changedFiles.Add(relativePath);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    changedFiles.Add(relativePath);
                }
            });

        return new GraphFreshnessResult(
            changedFiles.IsEmpty,
            changedFiles.OrderBy(file => file, StringComparer.Ordinal).ToArray());
    }
}

public sealed record GraphFreshnessResult(bool IsFresh, IReadOnlyList<string> ChangedFiles);

using System.Security.Cryptography;
using TraceLens.Analyzer.Models;

namespace TraceLens.Server;

public interface IGraphFreshnessService
{
    Task<GraphFreshnessResult> CheckAsync(GraphDocument graph, CancellationToken cancellationToken = default);
}

public sealed class GraphFreshnessService : IGraphFreshnessService
{
    public async Task<GraphFreshnessResult> CheckAsync(
        GraphDocument graph,
        CancellationToken cancellationToken = default)
    {
        var solutionDirectory = Path.GetDirectoryName(graph.SolutionPath)
            ?? throw new InvalidOperationException("솔루션 디렉터리를 확인할 수 없다.");
        var changedFiles = new List<string>();

        foreach (var (relativePath, expectedHash) in graph.FileHashes.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(solutionDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                await using var stream = File.OpenRead(fullPath);
                var hash = await SHA256.HashDataAsync(stream, cancellationToken);
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
        }

        return new GraphFreshnessResult(changedFiles.Count == 0, changedFiles);
    }
}

public sealed record GraphFreshnessResult(bool IsFresh, IReadOnlyList<string> ChangedFiles);

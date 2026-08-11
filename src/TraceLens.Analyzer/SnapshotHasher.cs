using System.Security.Cryptography;

namespace TraceLens.Analyzer;

public sealed class SnapshotHasher
{
    public async Task<IReadOnlyDictionary<string, string>> ComputeAsync(
        string solutionDirectory,
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in relativePaths.Distinct(StringComparer.Ordinal))
        {
            var fullPath = Path.Combine(solutionDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            await using var stream = File.OpenRead(fullPath);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            hashes[relativePath] = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }

        return hashes;
    }
}

using System.Security.Cryptography;
using System.Text;

namespace TraceLens.Analyzer;

public sealed class SnapshotHasher
{
    /// <summary>파일을 읽지 못했을 때 넣는 값. 해시가 없다는 사실 자체가 신선도 판정의 근거가 된다.</summary>
    public const string UnknownHash = "unknown";

    public async Task<IReadOnlyDictionary<string, string>> ComputeAsync(
        string solutionDirectory,
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in relativePaths.Distinct(StringComparer.Ordinal))
        {
            hashes[relativePath] = await ComputeFileAsync(
                Path.Combine(solutionDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                cancellationToken);
        }

        return hashes;
    }

    public static async Task<string> ComputeFileAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.OpenRead(fullPath);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UnknownHash;
        }
    }

    /// <summary>여러 해시를 순서에 무관하게 하나로 접는다. 같은 입력이면 항상 같은 지문이 나온다.</summary>
    public static string Fold(IEnumerable<string> parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts.OrderBy(item => item, StringComparer.Ordinal))
        {
            builder.Append(part).Append('\n');
        }

        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }
}

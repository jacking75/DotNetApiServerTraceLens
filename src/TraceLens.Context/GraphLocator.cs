using System.Text.Json;

namespace TraceLens.Context;

/// <param name="GraphPath">찾은 <c>graph.json</c>의 전체 경로. 찾지 못했으면 null이다.</param>
/// <param name="SearchedPaths">찾아본 경로들. 실패했을 때 사용자에게 그대로 보여준다.</param>
public sealed record GraphLocation(string? GraphPath, IReadOnlyList<string> SearchedPaths)
{
    public bool Found => GraphPath is not null;
}

/// <summary>
/// 분석 대상(.sln·.csproj) 하나에 대해 저장된 <c>.tracelens/graph.json</c>이 어디에 있는지 찾는다.
/// 분석기는 참조 프로젝트까지 담는 공통 상위 디렉터리에 결과를 쓰므로(<c>graph.rootDirectory</c>),
/// 대상 파일의 폴더에만 있다고 가정하면 형제 폴더에 참조 프로젝트가 있는 .csproj에서 결과를 다시 찾지 못한다.
/// 그래서 대상 폴더부터 조상 폴더를 올라가며 <c>solutionPath</c>가 대상과 같은 첫 파일을 고른다.
/// </summary>
public static class GraphLocator
{
    /// <summary>드라이브 루트까지 무한정 올라가지 않도록 둔 상한.</summary>
    public const int MaxAncestorLevels = 8;

    /// <summary>
    /// 헤더 확인에 읽는 최대 바이트. <c>schemaVersion</c>·<c>solutionPath</c>는 문서 맨 앞 두 속성이라
    /// 노드·엣지 배열을 읽지 않고도 확인할 수 있다.
    /// </summary>
    private const int HeaderPrefixBytes = 64 * 1024;

    /// <summary>
    /// 대상 파일의 폴더에서 시작해 조상 폴더를 올라가며 <c>.tracelens/graph.json</c>을 찾는다.
    /// 헤더만 읽어 대상과 같은지 확인하므로 3 MB짜리 그래프도 전부 읽지 않는다.
    /// 여러 개가 맞으면 대상에 가장 가까운 것을 돌려준다.
    /// </summary>
    public static async Task<GraphLocation> FindForTargetAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        var searched = new List<string>();
        string fullTargetPath;
        string? directory;
        try
        {
            fullTargetPath = Path.GetFullPath(targetPath);
            directory = Path.GetDirectoryName(fullTargetPath);
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return new GraphLocation(null, searched);
        }

        var current = directory is null ? null : new DirectoryInfo(directory);
        for (var level = 0; current is not null && level < MaxAncestorLevels; level++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, ".tracelens", "graph.json");
            searched.Add(candidate);
            if (!File.Exists(candidate))
            {
                continue;
            }

            var header = await ReadHeaderAsync(candidate, cancellationToken);
            if (header is not null && string.Equals(header.SolutionPath, fullTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                return new GraphLocation(candidate, searched);
            }
        }

        return new GraphLocation(null, searched);
    }

    /// <param name="SolutionPath">그래프가 기록한 분석 대상 경로.</param>
    public sealed record GraphHeader(int SchemaVersion, string SolutionPath);

    /// <summary>
    /// 파일 앞부분만 읽어 최상위 <c>schemaVersion</c>·<c>solutionPath</c>를 얻는다.
    /// 앞부분 안에서 두 값을 찾지 못하면(형식이 다르거나 속성 순서가 바뀐 파일) null을 돌려준다.
    /// </summary>
    public static async Task<GraphHeader?> ReadHeaderAsync(string graphPath, CancellationToken cancellationToken = default)
    {
        byte[] prefix;
        try
        {
            await using var stream = File.OpenRead(graphPath);
            var length = (int)Math.Min(stream.Length, HeaderPrefixBytes);
            prefix = new byte[length];
            var read = await stream.ReadAtLeastAsync(prefix, length, throwOnEndOfStream: false, cancellationToken);
            if (read < length)
            {
                prefix = prefix[..read];
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return ParseHeader(prefix);
    }

    /// <summary>앞부분 바이트에서 최상위 속성 두 개를 읽는다. 중첩된 값은 건너뛴다.</summary>
    private static GraphHeader? ParseHeader(ReadOnlySpan<byte> prefix)
    {
        var reader = new Utf8JsonReader(prefix, isFinalBlock: false, state: default);
        var schemaVersion = 0;
        string? solutionPath = null;
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                {
                    continue;
                }

                var isSchema = reader.ValueTextEquals("schemaVersion");
                var isSolution = reader.ValueTextEquals("solutionPath");
                if (!reader.Read())
                {
                    break;
                }

                if (isSchema && reader.TokenType == JsonTokenType.Number)
                {
                    schemaVersion = reader.GetInt32();
                }
                else if (isSolution && reader.TokenType == JsonTokenType.String)
                {
                    solutionPath = reader.GetString();
                }
                else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray && !reader.TrySkip())
                {
                    // 앞부분에 다 담기지 않은 배열이면 더 읽을 것이 없다.
                    break;
                }

                if (solutionPath is not null && schemaVersion > 0)
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return solutionPath is null ? null : new GraphHeader(schemaVersion, solutionPath);
    }
}

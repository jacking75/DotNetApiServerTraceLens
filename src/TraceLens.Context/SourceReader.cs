using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

/// <param name="SourceVersion">
/// <c>snapshot</c>이면 분석 당시와 해시가 같은 파일이고, <c>current</c>면 분석 이후에 바뀐 현재 소스다.
/// 오래된 위치에 현재 코드를 붙여 놓고 "분석 당시 코드"라고 부르지 않기 위한 구분이다.
/// </param>
public sealed record SourceSlice(
    string File,
    int StartLine,
    int EndLine,
    string Content,
    bool Truncated,
    string SourceVersion);

public sealed record SourceReadResult(SourceSlice? Slice, SourceAccessResult Access);

/// <summary>
/// 스냅샷의 소스 접근 규칙을 지키며 파일 일부를 읽는다.
/// 최근 신선도 검사 결과만 믿지 않고, 실제로 돌려주는 시점에 해당 파일의 해시를 다시 확인한다.
/// </summary>
public sealed class SourceReader(GraphDocument graph, SourceAccessPolicy policy)
{
    public SourceAccessPolicy Policy { get; } = policy;

    public async Task<SourceReadResult> ReadAsync(
        string file,
        int startLine,
        int endLine,
        CancellationToken cancellationToken = default)
    {
        if (startLine < 1 || endLine < startLine)
        {
            return new SourceReadResult(
                null,
                SourceAccessResult.Deny(SourceAccessDenial.InvalidRange, "startLine, endLine 값을 확인해야 한다."));
        }

        var access = Policy.Check(file);
        if (!access.Allowed)
        {
            return new SourceReadResult(null, access);
        }

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(access.FullPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 편집기가 잡고 있거나 권한이 없는 파일이다. 500으로 새지 않도록 거부로 바꾼다.
            return new SourceReadResult(
                null,
                access with
                {
                    Allowed = false,
                    Denial = SourceAccessDenial.Unreadable,
                    Message = $"소스를 읽지 못했다: {exception.Message}"
                });
        }
        if (startLine > lines.Length)
        {
            return new SourceReadResult(
                null,
                SourceAccessResult.Deny(SourceAccessDenial.InvalidRange, "시작 라인이 파일 범위를 벗어났다."));
        }

        var requestedEnd = Math.Min(endLine, lines.Length);
        var cappedEnd = Math.Min(requestedEnd, startLine + SourceAccessPolicy.MaxResponseLines - 1);
        var content = string.Join(Environment.NewLine, lines[(startLine - 1)..cappedEnd]);
        var truncated = cappedEnd < requestedEnd;
        if (content.Length > SourceAccessPolicy.MaxResponseBytes)
        {
            content = content[..SourceAccessPolicy.MaxResponseBytes];
            truncated = true;
        }

        var currentHash = await SnapshotHasher.ComputeFileAsync(access.FullPath, cancellationToken);
        var recordedHash = graph.FileHashes.TryGetValue(access.RelativePath, out var expected) ? expected : null;
        var sourceVersion = recordedHash is not null && string.Equals(recordedHash, currentHash, StringComparison.Ordinal)
            ? "snapshot"
            : "current";

        return new SourceReadResult(
            new SourceSlice(access.RelativePath, startLine, cappedEnd, content, truncated, sourceVersion),
            access);
    }
}

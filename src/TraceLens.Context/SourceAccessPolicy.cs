using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

/// <summary>소스 읽기가 거절된 이유.</summary>
public enum SourceAccessDenial
{
    None,
    OutsideRoot,
    NotInSnapshot,
    NotFound,
    TooLarge,
    InvalidRange,

    /// <summary>경로는 허용됐지만 파일을 읽지 못했다(잠김·권한).</summary>
    Unreadable
}

public sealed record SourceAccessResult(
    bool Allowed,
    SourceAccessDenial Denial,
    string Message,
    string FullPath,
    string RelativePath)
{
    public static SourceAccessResult Deny(SourceAccessDenial denial, string message) =>
        new(false, denial, message, string.Empty, string.Empty);
}

/// <summary>
/// Server와 MCP가 함께 쓰는 소스 접근 규칙이다.
/// 정규화한 루트 안인지, 이 스냅샷이 실제로 분석한 파일인지, 심볼릭 링크·정션의 실제 대상이 루트 안인지 확인한다.
/// 가져온 JSON의 <c>rootDirectory</c>만 믿고 로컬 파일 접근 권한을 주지 않기 위한 장치다.
/// </summary>
public sealed class SourceAccessPolicy
{
    /// <summary>한 번에 돌려줄 수 있는 소스의 최대 바이트.</summary>
    public const int MaxResponseBytes = 512 * 1024;

    /// <summary>한 번에 돌려줄 수 있는 최대 줄 수.</summary>
    public const int MaxResponseLines = 4000;

    private readonly string normalizedRoot;
    private readonly HashSet<string> allowedFiles;

    private SourceAccessPolicy(string normalizedRoot, HashSet<string> allowedFiles, bool trusted)
    {
        this.normalizedRoot = normalizedRoot;
        this.allowedFiles = allowedFiles;
        IsTrusted = trusted;
    }

    /// <summary>루트가 확인된(=이 기계에서 실제로 분석한) 스냅샷인지. 가져온 그래프는 false다.</summary>
    public bool IsTrusted { get; }

    public string Root => normalizedRoot;

    /// <summary>분석 결과에 담긴 파일 목록만 열 수 있는 정책을 만든다.</summary>
    public static SourceAccessPolicy ForGraph(GraphDocument graph, bool trusted = true)
    {
        var root = string.IsNullOrEmpty(graph.RootDirectory)
            ? string.Empty
            : Normalize(graph.RootDirectory);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in graph.FileHashes.Keys)
        {
            allowed.Add(file.Replace('\\', '/'));
        }

        foreach (var node in graph.Nodes)
        {
            allowed.Add(node.File.Replace('\\', '/'));
        }

        foreach (var item in graph.Classes)
        {
            allowed.Add(item.File.Replace('\\', '/'));
        }

        if (graph.StartupFlow is not null)
        {
            allowed.Add(graph.StartupFlow.ProgramFile.Replace('\\', '/'));
        }

        foreach (var input in graph.Manifest?.Inputs ?? [])
        {
            allowed.Add(input.Path.Replace('\\', '/'));
        }

        return new SourceAccessPolicy(root, allowed, trusted);
    }

    public SourceAccessResult Check(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file))
        {
            return SourceAccessResult.Deny(SourceAccessDenial.OutsideRoot, "분석 루트 기준 상대 경로만 읽을 수 있다.");
        }

        if (normalizedRoot.Length == 0)
        {
            return SourceAccessResult.Deny(SourceAccessDenial.OutsideRoot, "분석 루트를 확인할 수 없다.");
        }

        if (!IsTrusted)
        {
            return SourceAccessResult.Deny(
                SourceAccessDenial.OutsideRoot,
                "가져온 분석 결과(offlineSnapshot)라 로컬 소스를 읽지 않는다. 원본 루트를 연결하고 입력 일치를 확인해야 한다.");
        }

        var normalizedFile = file.Replace('\\', '/');
        if (!allowedFiles.Contains(normalizedFile))
        {
            return SourceAccessResult.Deny(
                SourceAccessDenial.NotInSnapshot,
                "이 스냅샷이 분석한 파일 목록에 없다.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, normalizedFile.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsideRoot(fullPath))
        {
            return SourceAccessResult.Deny(SourceAccessDenial.OutsideRoot, "분석 루트 밖 파일은 읽을 수 없다.");
        }

        if (!File.Exists(fullPath))
        {
            return SourceAccessResult.Deny(SourceAccessDenial.NotFound, "소스 파일을 찾을 수 없다.");
        }

        // 심볼릭 링크·Windows 정션은 실제 대상 경로까지 확인한다.
        var resolved = ResolveLinkTarget(fullPath);
        return IsInsideRoot(resolved)
            ? new SourceAccessResult(true, SourceAccessDenial.None, string.Empty, fullPath, normalizedFile)
            : SourceAccessResult.Deny(SourceAccessDenial.OutsideRoot, "링크가 분석 루트 밖을 가리킨다.");
    }

    private bool IsInsideRoot(string fullPath)
    {
        var relative = Path.GetRelativePath(normalizedRoot, fullPath);
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string ResolveLinkTarget(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? fullPath : Path.GetFullPath(target.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return fullPath;
        }
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            var info = new DirectoryInfo(full);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? full : Path.GetFullPath(target.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return full;
        }
    }
}

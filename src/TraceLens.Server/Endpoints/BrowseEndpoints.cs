using TraceLens.Analyzer;

namespace TraceLens.Server.Endpoints;

/// <summary>
/// 분석 대상 .sln/.csproj를 고르기 위한 로컬 파일 시스템 탐색 API.
/// 브라우저의 파일 선택 대화상자는 절대 경로를 알려주지 않으므로 서버가 디렉터리 목록을 제공한다.
/// 서버 자체가 127.0.0.1에만 바인딩되므로(설계 제약 4번) 로컬 사용자만 접근한다.
/// </summary>
public static class BrowseEndpoints
{
    /// <summary>탐색해도 분석 대상이 나오지 않고 목록만 길어지는 디렉터리.</summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".vscode", ".idea", ".tracelens"
    };

    public static RouteGroupBuilder MapBrowseEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/browse", Browse);
        return group;
    }

    internal static IResult Browse(string? path)
    {
        string? directory;
        try
        {
            directory = ResolveDirectory(path);
        }
        catch (PathTooLongException exception)
        {
            // 조용히 드라이브 목록으로 돌아가면 "찾아보기가 갑자기 처음으로 간다"로 보인다.
            return ApiResults.Error(
                StatusCodes.Status400BadRequest,
                "PATH_TOO_LONG",
                "경로가 너무 길다. Windows 긴 경로 지원을 켜거나 더 짧은 위치로 옮겨야 한다.",
                new { detail = exception.Message });
        }

        if (directory is null)
        {
            return Results.Ok(new BrowseResponse(null, null, GetRoots()));
        }

        try
        {
            var info = new DirectoryInfo(directory);
            var files = info.EnumerateFiles()
                .Select(file => (File: file, Kind: GetTargetKind(file.Name)))
                .Where(item => item.Kind is not null)
                .OrderBy(item => item.File.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new BrowseEntry(item.File.Name, item.File.FullName, item.Kind!.Value)
                {
                    Reason = item.Kind == BrowseEntryKind.Unsupported
                        ? SolutionLoader.UnsupportedExtensions[Path.GetExtension(item.File.Name).ToLowerInvariant()]
                        : string.Empty
                });
            var directories = info.EnumerateDirectories()
                .Where(child => !child.Attributes.HasFlag(FileAttributes.Hidden) && !SkippedDirectories.Contains(child.Name))
                .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
                .Select(child => new BrowseEntry(child.Name, child.FullName, BrowseEntryKind.Directory));

            return Results.Ok(new BrowseResponse(
                info.FullName,
                info.Parent?.FullName,
                [.. files, .. directories]));
        }
        catch (UnauthorizedAccessException)
        {
            return ApiResults.LegacyError(
                StatusCodes.Status400BadRequest, ApiResults.SourceDenied, "이 디렉터리를 읽을 권한이 없다.");
        }
        catch (IOException exception)
        {
            return ApiResults.LegacyError(
                StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, exception.Message);
        }
    }

    /// <summary>입력 경로에서 실제로 열 수 있는 디렉터리를 찾는다. 파일이면 그 파일이 있는 디렉터리를, 없는 경로면 가장 가까운 상위 디렉터리를 돌려준다.</summary>
    private static string? ResolveDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? candidate;
        try
        {
            candidate = Path.GetFullPath(path.Trim());
        }
        catch (PathTooLongException)
        {
            // 호출자가 오류 코드로 바꾼다. 여기서 null을 돌려주면 원인이 사라진다.
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }

        if (File.Exists(candidate))
        {
            candidate = Path.GetDirectoryName(candidate);
        }

        while (!string.IsNullOrEmpty(candidate) && !Directory.Exists(candidate))
        {
            candidate = Path.GetDirectoryName(candidate);
        }

        return string.IsNullOrEmpty(candidate) ? null : candidate;
    }

    /// <summary>
    /// 목록에 보여 줄 파일 종류. 지원하지 않는 형식(<c>.slnx</c> 등)도 <see cref="BrowseEntryKind.Unsupported"/>로 보여 준다 —
    /// 목록에서 아예 빼면 <c>.slnx</c>만 있는 폴더가 빈 폴더로 보이고, 잘 써 둔 미지원 사유에 닿을 수 없다.
    /// </summary>
    private static BrowseEntryKind? GetTargetKind(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is ".sln")
        {
            return BrowseEntryKind.Solution;
        }

        if (extension is ".csproj")
        {
            return BrowseEntryKind.Project;
        }

        return SolutionLoader.UnsupportedExtensions.ContainsKey(extension) ? BrowseEntryKind.Unsupported : null;
    }

    private static IReadOnlyList<BrowseEntry> GetRoots()
    {
        var roots = new List<BrowseEntry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                {
                    roots.Add(new BrowseEntry(drive.Name, drive.RootDirectory.FullName, BrowseEntryKind.Directory));
                }
            }
            catch (IOException)
            {
                // 준비되지 않은 드라이브는 목록에서 뺀다.
            }
        }

        return roots;
    }
}

public enum BrowseEntryKind
{
    Directory,
    Solution,
    Project,

    /// <summary>아직 지원하지 않는 형식. 목록에는 보여 주되 고를 수는 없고 사유를 함께 준다.</summary>
    Unsupported
}

/// <param name="Reason">지원하지 않는 형식일 때의 사유. 그 밖에는 빈 문자열이다.</param>
public sealed record BrowseEntry(string Name, string Path, BrowseEntryKind Kind)
{
    public string Reason { get; init; } = string.Empty;
}

/// <param name="Path">지금 보고 있는 디렉터리. 드라이브 목록을 보여줄 때는 null이다.</param>
/// <param name="ParentPath">상위 디렉터리. 드라이브 루트면 null이다.</param>
public sealed record BrowseResponse(string? Path, string? ParentPath, IReadOnlyList<BrowseEntry> Entries);

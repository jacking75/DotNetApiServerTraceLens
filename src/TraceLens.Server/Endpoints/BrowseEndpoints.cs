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
        var directory = ResolveDirectory(path);
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
                .Select(item => new BrowseEntry(item.File.Name, item.File.FullName, item.Kind!.Value));
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
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
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

    private static BrowseEntryKind? GetTargetKind(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".sln" => BrowseEntryKind.Solution,
        ".csproj" => BrowseEntryKind.Project,
        _ => null
    };

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
    Project
}

public sealed record BrowseEntry(string Name, string Path, BrowseEntryKind Kind);

/// <param name="Path">지금 보고 있는 디렉터리. 드라이브 목록을 보여줄 때는 null이다.</param>
/// <param name="ParentPath">상위 디렉터리. 드라이브 루트면 null이다.</param>
public sealed record BrowseResponse(string? Path, string? ParentPath, IReadOnlyList<BrowseEntry> Entries);

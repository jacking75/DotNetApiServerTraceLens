namespace TraceLens.Analyzer;

internal static class SourcePaths
{
    /// <summary>솔루션 디렉터리 기준 상대 경로('/' 구분자)를 돌려주고, 솔루션 밖의 파일이면 null을 돌려준다.</summary>
    public static string? ToSolutionRelative(string solutionDirectory, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        string relativePath;
        try
        {
            // source generator가 만든 문서는 파일 경로가 아니라 이름일 수 있다(잘못된 문자 포함).
            relativePath = Path.GetRelativePath(solutionDirectory, Path.GetFullPath(filePath));
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        if (relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>obj/bin 아래의 빌드 산출물(자동 생성 소스 포함)인지 확인한다.</summary>
    public static bool IsBuildOutput(string relativePath) => relativePath
        .Split('/')
        .Any(segment =>
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase));
}

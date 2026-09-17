namespace TraceLens.Server.Endpoints;

/// <summary>
/// 사용 설명서(<c>docs/manual*.html</c>)를 앱 안에서 열 수 있게 서빙한다.
/// 루프백 전용 서버라 외부에 공개되지 않는다.
/// </summary>
public static class ManualEndpoints
{
    /// <summary>언어 코드 → 배포물 안의 파일 이름.</summary>
    public static readonly IReadOnlyDictionary<string, string> Files =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ko"] = "manual_kr.html",
            ["en"] = "manual.html",
        };

    public static IEndpointRouteBuilder MapManualEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/manual/{lang}", async Task<IResult> (string lang, HttpContext context) =>
        {
            if (!Files.TryGetValue(lang, out var fileName))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "MANUAL_NOT_FOUND",
                    $"지원하지 않는 설명서 언어다: {lang}");
            }

            if (ResolveManualPath(fileName) is not { } path)
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "MANUAL_NOT_FOUND",
                    $"설명서 파일을 찾지 못했다: {fileName}. 배포물에 docs/{fileName}이 함께 들어가야 한다.");
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(path);
            return Results.Empty;
        });

        return app;
    }

    /// <summary>
    /// 배포물(실행 파일 옆 <c>docs/</c>)과 저장소(솔루션 루트의 <c>docs/</c>) 양쪽에서 찾는다.
    /// 개발 중에는 작업 디렉터리가 프로젝트 폴더라 위로 올라가며 찾는다.
    /// </summary>
    public static string? ResolveManualPath(string fileName)
    {
        foreach (var root in CandidateRoots())
        {
            var path = Path.Combine(root, "docs", fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return AppContext.BaseDirectory;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && directory.Parent is not null; depth++)
        {
            directory = directory.Parent;
            yield return directory.FullName;
        }

        yield return Directory.GetCurrentDirectory();
    }
}

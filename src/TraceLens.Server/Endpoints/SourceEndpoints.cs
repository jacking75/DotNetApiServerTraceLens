namespace TraceLens.Server.Endpoints;

public static class SourceEndpoints
{
    public static RouteGroupBuilder MapSourceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/source", ReadSourceAsync);
        return group;
    }

    private static async Task<IResult> ReadSourceAsync(
        string file,
        int start,
        int end,
        IGraphStore store,
        CancellationToken cancellationToken)
    {
        var graph = store.Current;
        if (graph is null)
        {
            return Results.NotFound(new { error = "먼저 솔루션을 분석해야 한다." });
        }

        if (string.IsNullOrWhiteSpace(file) || start < 1 || end < start)
        {
            return Results.BadRequest(new { error = "file, start, end 값을 확인해야 한다." });
        }

        var solutionDirectory = graph.RootDirectory;
        var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, file));
        var relativePath = Path.GetRelativePath(solutionDirectory, fullPath);
        if (Path.IsPathRooted(file) || relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "솔루션 외부 파일은 읽을 수 없다." });
        }

        if (!File.Exists(fullPath))
        {
            return Results.NotFound(new { error = "소스 파일을 찾을 수 없다." });
        }

        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        if (start > lines.Length)
        {
            return Results.BadRequest(new { error = "시작 라인이 파일 범위를 벗어났다." });
        }

        var actualEnd = Math.Min(end, lines.Length);
        return Results.Ok(new SourceResponse(
            relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            start,
            actualEnd,
            string.Join(Environment.NewLine, lines[(start - 1)..actualEnd])));
    }
}

public sealed record SourceResponse(string File, int StartLine, int EndLine, string Content);

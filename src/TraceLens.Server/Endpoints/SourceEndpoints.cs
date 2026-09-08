using TraceLens.Context;

namespace TraceLens.Server.Endpoints;

public static class SourceEndpoints
{
    public static RouteGroupBuilder MapSourceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/source", ReadSourceAsync);
        return group;
    }

    /// <summary>
    /// 분석 루트 안의 소스를 읽는다. 접근 규칙은 MCP와 같은 <see cref="SourceAccessPolicy"/>를 쓴다.
    /// 응답에는 이 내용이 분석 당시와 같은지(<c>sourceVersion</c>)를 함께 담는다.
    /// </summary>
    private static async Task<IResult> ReadSourceAsync(
        string file,
        int start,
        int end,
        IGraphStore store,
        CancellationToken cancellationToken)
    {
        var session = store.Session;
        if (session is null)
        {
            return ApiResults.NotAnalyzed();
        }

        var result = await session.Source.ReadAsync(file, start, end, cancellationToken);
        if (result.Slice is null)
        {
            var statusCode = result.Access.Denial switch
            {
                SourceAccessDenial.NotFound => StatusCodes.Status404NotFound,
                SourceAccessDenial.NotInSnapshot => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest
            };
            return ApiResults.LegacyError(statusCode, ApiResults.SourceDenied, result.Access.Message);
        }

        var slice = result.Slice;
        return Results.Ok(new SourceResponse(
            slice.File,
            slice.StartLine,
            slice.EndLine,
            slice.Content,
            slice.SourceVersion,
            slice.Truncated));
    }
}

/// <param name="SourceVersion">snapshot이면 분석 당시와 같은 내용이고, current면 그 뒤로 바뀐 현재 소스다.</param>
public sealed record SourceResponse(
    string File,
    int StartLine,
    int EndLine,
    string Content,
    string SourceVersion,
    bool Truncated);

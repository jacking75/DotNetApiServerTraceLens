using TraceLens.Context;

namespace TraceLens.Server;

/// <param name="Code">UI 번역과 MCP가 같은 의미로 쓰는 오류 코드.</param>
public sealed record ApiError(string Code, string Message, object? Details = null);

/// <summary>
/// 새 API의 오류를 <c>{ code, message, details }</c>로 통일한다.
/// 과거 API는 <c>error</c> 필드를 유지한 채 code를 함께 담아 호환을 깨지 않는다.
/// </summary>
public static class ApiResults
{
    public const string NoAnalysis = "NO_ANALYSIS";
    public const string SnapshotMismatch = "SNAPSHOT_MISMATCH";
    public const string SelectionNotFound = "SELECTION_NOT_FOUND";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string SourceDenied = "SOURCE_DENIED";
    public const string AnalyzeFailed = "ANALYZE_FAILED";

    public static IResult Error(int statusCode, string code, string message, object? details = null) =>
        Results.Json(new ApiError(code, message, details), statusCode: statusCode);

    /// <summary>과거 응답 형태(<c>error</c>)를 유지하면서 code를 함께 돌려준다.</summary>
    public static IResult LegacyError(int statusCode, string code, string message, object? details = null) =>
        Results.Json(new { error = message, code, message, details }, statusCode: statusCode);

    public static IResult NotAnalyzed() =>
        LegacyError(StatusCodes.Status404NotFound, NoAnalysis, "먼저 솔루션을 분석해야 한다.");

    public static IResult FromContextException(ContextException exception) => exception.Code switch
    {
        SnapshotMismatch => Error(StatusCodes.Status409Conflict, exception.Code, exception.Message),
        SelectionNotFound => Error(StatusCodes.Status404NotFound, exception.Code, exception.Message),
        _ => Error(StatusCodes.Status400BadRequest, exception.Code, exception.Message)
    };
}

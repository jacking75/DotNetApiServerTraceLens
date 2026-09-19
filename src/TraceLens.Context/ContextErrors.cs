namespace TraceLens.Context;

/// <summary>
/// 서버(HTTP)와 MCP가 <b>같은 입력에 같은 코드</b>를 돌려주도록 한곳에 모은 오류 코드다.
///
/// 예전에는 MCP가 <see cref="KeyNotFoundException"/>·<see cref="ArgumentException"/>을 그대로 던져
/// 에이전트가 받는 것은 예외 문자열뿐이었다. 서버는 <c>{code,message,details}</c>를 주는데
/// 같은 상황에서 형태가 갈리면 "무엇 때문에 막혔는지"를 코드로 분기할 수 없다(T60).
/// </summary>
public static class ContextErrorCodes
{
    /// <summary>분석 결과가 없다. 사용자가 먼저 분석해야 한다.</summary>
    public const string NoAnalysis = "NO_ANALYSIS";

    /// <summary>기대한 스냅샷과 현재 분석이 다르다. 그 사이에 재분석이 있었다.</summary>
    public const string SnapshotMismatch = "SNAPSHOT_MISMATCH";

    /// <summary>선택한 항목(노드·엔드포인트·클래스…)을 찾지 못했다.</summary>
    public const string SelectionNotFound = "SELECTION_NOT_FOUND";

    /// <summary>선택 종류가 지원 목록에 없다.</summary>
    public const string SelectionKind = "SELECTION_KIND";

    /// <summary>요청 인자가 잘못됐다(빈 질의·범위를 벗어난 값 등).</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary>소스 접근 정책이 막았다. 사유는 details에 담는다.</summary>
    public const string SourceDenied = "SOURCE_DENIED";

    /// <summary>분석 자체가 실패했다.</summary>
    public const string AnalyzeFailed = "ANALYZE_FAILED";

    /// <summary>그래프 파일을 읽을 수 없다(깨졌거나 미래 스키마다).</summary>
    public const string GraphUnreadable = "GRAPH_UNREADABLE";

    /// <summary>기준 스냅샷을 찾지 못했다.</summary>
    public const string SnapshotNotFound = "SNAPSHOT_NOT_FOUND";

    /// <summary>알 수 없는 실패. 코드로 분기할 수 없을 때만 쓴다.</summary>
    public const string Internal = "INTERNAL";

    /// <summary>
    /// 흔한 .NET 예외를 코드로 옮긴다. MCP 도구가 예외를 잡아 <c>{error:{code,…}}</c>로 바꿀 때 쓴다.
    /// 여기에 없는 예외는 <see cref="Internal"/>이다 — 모르는 것을 아는 척 분류하지 않는다.
    /// </summary>
    public static string FromException(Exception exception) => exception switch
    {
        ContextException context => context.Code,
        KeyNotFoundException => SelectionNotFound,
        ArgumentException => InvalidRequest,
        InvalidDataException => GraphUnreadable,
        FileNotFoundException => NoAnalysis,
        DirectoryNotFoundException => NoAnalysis,
        UnauthorizedAccessException => SourceDenied,
        _ => Internal
    };
}

/// <summary>
/// MCP 도구가 실패했을 때 돌려주는 모양. 서버의 <c>{code,message,details}</c>와 같은 필드다.
/// </summary>
public sealed record ToolErrorBody(string Code, string Message, object? Details = null);

/// <param name="Error">실패 사유. 성공하면 이 필드가 없는 결과를 그대로 돌려준다.</param>
public sealed record ToolError(ToolErrorBody Error)
{
    /// <summary>MCP 규약의 오류 표시. 클라이언트가 결과 본문을 읽기 전에 실패를 알 수 있다.</summary>
    public bool IsError { get; init; } = true;

    public static ToolError From(Exception exception, object? details = null) =>
        new(new ToolErrorBody(ContextErrorCodes.FromException(exception), exception.Message, details));
}

/// <summary>
/// MCP 도구 결과의 공통 뼈대. 실패해도 <b>예외 대신</b> 같은 타입에 <see cref="Error"/>를 채워 돌려준다.
/// 에이전트가 성공·실패를 같은 스키마로 읽을 수 있어야 코드로 분기할 수 있다(T60).
/// </summary>
public abstract record ToolResult
{
    /// <summary>실패 사유. 성공하면 null이다.</summary>
    public ToolErrorBody? Error { get; init; }

    /// <summary>MCP 규약의 오류 표시.</summary>
    public bool IsError => Error is not null;
}

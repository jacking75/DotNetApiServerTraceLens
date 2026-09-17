namespace TraceLens.Analyzer.Models;

public enum MiddlewareType
{
    /// <summary>솔루션 안에 소스가 있는 미들웨어. 본문을 열어 요청/응답 구간을 나눌 수 있다.</summary>
    Custom,

    /// <summary><c>UseRouting</c>·<c>UseAuthentication</c>처럼 프레임워크가 제공하는 것. 이름과 순서만 안다.</summary>
    Framework
}

public enum ContextAccessMode
{
    Read,
    Write
}

/// <param name="StatusCode">그 분기가 넣는 상태 코드 상수. 찾지 못하면 빈 문자열이다.</param>
public sealed record MiddlewareShortCircuit(string StatusCode, string Condition, int Line);

/// <param name="Target"><c>Items["UserNo"]</c>, <c>Response.Headers["X-Elapsed"]</c> 같은 표시 문자열.</param>
public sealed record ContextAccess(ContextAccessMode Mode, string Target);

/// <param name="StatusCode">이 필터가 끊을 때 쓰는 상태 코드. 끊지 않으면 null.</param>
public sealed record EndpointFilterInfo(string Name, string Detail, string? StatusCode);

/// <param name="Pre"><c>_next</c> 호출 전에 하는 일. 프레임워크 미들웨어는 알려진 설명을 쓴다.</param>
/// <param name="Post"><c>_next</c> 이후(응답이 돌아올 때) 하는 일. 없으면 null.</param>
/// <param name="Condition"><c>UseWhen</c>이나 <c>if (env.IsDevelopment())</c>처럼 조건부로 등록됐을 때 그 조건 문자열.</param>
/// <param name="IsTerminal">요청을 끝내는 종단 단계인지(<c>UseEndpoints</c>·<c>app.Run(delegate)</c>).</param>
/// <param name="IsDead">종단 뒤에 등록되어 실행되지 않는지.</param>
public sealed record MiddlewareStepInfo(
    string Id,
    int Order,
    string Name,
    MiddlewareType Type,
    string RegistrationFile,
    int RegistrationLine,
    string? ClassId,
    string? ImplementationFile,
    int ImplementationLine,
    string? Pre,
    string? Post,
    MiddlewareShortCircuit? ShortCircuit,
    IReadOnlyList<ContextAccess> ContextAccesses,
    IReadOnlyList<string> StoreAccesses,
    IReadOnlyList<EndpointFilterInfo> Filters,
    bool IsTerminal,
    bool IsDead,
    string? Condition,
    string? Note)
{
    /// <summary>
    /// 이 단계가 미들웨어인지, 엔드포인트 등록인지, 종단인지.
    /// <c>MapControllers()</c>·<c>MapGet()</c>은 <b>등록</b>이지 미들웨어가 아니다. 엔드포인트 실행은 파이프라인 맨 끝에서 일어나므로
    /// 그 뒤에 등록한 미들웨어도 정상적으로 실행된다. v7 이전 문서에는 없고, 없으면 Middleware로 읽는다.
    /// </summary>
    public MiddlewareStepKind Kind { get; init; } = MiddlewareStepKind.Middleware;
}

/// <summary>파이프라인 한 줄의 성격.</summary>
public enum MiddlewareStepKind
{
    /// <summary>요청을 순서대로 통과하는 미들웨어.</summary>
    Middleware,

    /// <summary>엔드포인트 등록(<c>MapGet</c>·<c>MapControllers</c>·<c>MapOpenApi</c> 등). 실행은 파이프라인 맨 끝이다.</summary>
    EndpointRegistration,

    /// <summary>요청을 여기서 끝내는 종단(<c>UseEndpoints</c>·<c>app.Run(delegate)</c>).</summary>
    Terminal
}

/// <summary>미들웨어 보기 전체. Program.cs를 찾지 못했거나 설정에서 껐으면 null이다.</summary>
public sealed record MiddlewarePipelineDocument(
    string ProgramFile,
    IReadOnlyList<MiddlewareStepInfo> Steps,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<RuleInfo> Rules);

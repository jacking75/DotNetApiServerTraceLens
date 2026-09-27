namespace TraceLens.Analyzer.Models;

/// <summary>시작 흐름 한 단계의 종류.</summary>
public enum StartupStepKind
{
    /// <summary><c>AddScoped</c>/<c>AddSingleton</c>/<c>AddTransient</c>(TryAdd 포함) 같은 DI 등록 호출.</summary>
    Registration,

    /// <summary>DI 등록 외의 설정 호출 또는 Program.cs의 최상위 초기화 문장.</summary>
    Configuration,

    /// <summary>솔루션 안에 정의된 확장 메서드 호출. 그 본문에서 찾은 하위 단계를 <see cref="StartupStep.Children"/>에 담는다.</summary>
    ExtensionCall,

    /// <summary><c>app.UseXxx()</c>/<c>MapXxx()</c> 같은 미들웨어·라우팅 파이프라인 호출.</summary>
    Middleware
}

/// <summary>DI 등록의 수명.</summary>
public enum StartupLifetime
{
    Scoped,
    Singleton,
    Transient
}

/// <summary>단계가 속한 if/else 경로. 정적 소스 구조이며 실행 여부를 뜻하지 않는다.</summary>
public sealed record StartupCondition(string Key, string Expression, bool WhenTrue, string File, int Line);

/// <param name="Label">화면에 보여줄 한 줄. 등록은 "서비스 → 구현체", 그 외에는 <c>메서드(인자)</c> 형태다.</param>
/// <param name="ServiceType">등록의 서비스(인터페이스/추상) 타입 표시 이름. 자기 자신을 등록하면(예: <c>AddScoped&lt;Foo&gt;()</c>) null이다.</param>
/// <param name="ServiceTypeId">서비스 타입이 솔루션 안에 있으면 그 클래스 id, 아니면 null.</param>
/// <param name="ImplementationTypeId">구현 타입이 솔루션 안에 있으면 그 클래스 id, 아니면 null.</param>
/// <param name="Children">ExtensionCall일 때 그 메서드 본문에서 찾은 하위 단계. 그 외에는 빈 목록이다.</param>
public sealed record StartupStep(
    StartupStepKind Kind,
    string Label,
    string? ServiceType,
    string? ServiceTypeId,
    string? ImplementationType,
    string? ImplementationTypeId,
    StartupLifetime? Lifetime,
    string File,
    int Line,
    IReadOnlyList<StartupStep> Children)
{
    private readonly IReadOnlyList<StartupStep> children = Children ?? [];

    public IReadOnlyList<StartupStep> Children
    {
        get => children;
        init => children = value ?? [];
    }

    /// <summary>바깥쪽부터 안쪽 순서로 나열한 조건 경로.</summary>
    public IReadOnlyList<StartupCondition> Conditions { get; init; } = [];
}

/// <summary>
/// Program.cs 하나를 분석한 시작 흐름. 앱 생성 전 문장은 <see cref="ServiceSteps"/>에,
/// 앱 생성 이후 문장과 미들웨어·라우팅 호출은 <see cref="MiddlewareSteps"/>에 소스 순서대로 담는다.
/// Program.cs를 찾지 못하면(구식 Startup.cs 등) 전체가 null이다.
/// </summary>
public sealed record StartupFlowDocument(
    string ProgramFile,
    IReadOnlyList<StartupStep> ServiceSteps,
    IReadOnlyList<StartupStep> MiddlewareSteps)
{
    /// <summary>이 시작 흐름이 속한 프로젝트(어셈블리 이름). 실행 가능한 프로젝트가 여럿일 때 구분한다.</summary>
    public string ProjectKey { get; init; } = string.Empty;

    /// <summary>
    /// 상한에 걸려 그리지 않은 것과 여러 <c>Startup</c> 중 건너뛴 파일.
    /// 로그에만 남기면 화면과 에이전트는 "그게 전부"라고 읽는다(T58).
    /// </summary>
    public IReadOnlyList<string> Limitations { get; init; } = [];
}

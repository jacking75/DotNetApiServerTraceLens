namespace TraceLens.Analyzer.Models;

/// <summary>진입점을 어디에서 찾았는지.</summary>
public enum EndpointSource
{
    /// <summary>컨트롤러 메서드의 <c>[HttpGet]</c> 같은 특성에서 찾았다.</summary>
    Controller,

    /// <summary><c>app.MapGet(...)</c> 같은 Minimal API 등록식에서 찾았다.</summary>
    MinimalApi
}

/// <summary>핸들러 본문이 어떤 모양인지. 람다·로컬 함수는 메서드 심볼 ID가 없어 구조 기반 ID를 쓴다.</summary>
public enum HandlerKind
{
    Method,
    MethodGroup,
    Lambda,
    LocalFunction,
    Unresolved
}

/// <summary>
/// HTTP 진입점 하나. <see cref="NodeId"/>(핸들러)와 <see cref="EndpointId"/>(엔드포인트)를 분리해서
/// 같은 메서드에 여러 라우트가 걸린 경우를 잃지 않는다.
/// </summary>
/// <param name="NodeId">핸들러 노드 ID. 여러 엔드포인트가 같은 값을 가질 수 있다.</param>
public sealed record EntryPoint(string NodeId, string HttpMethod, string Route)
{
    private readonly IReadOnlyList<string> notes = [];

    /// <summary>
    /// <c>projectKey|httpMethod|normalizedRoute|registrationIdentity</c> 형태의 안정 ID다.
    /// v6 이하로 저장된 그래프에는 없으므로 읽는 쪽에서 라우트·동사·nodeId로 호환 생성한다.
    /// </summary>
    public string EndpointId { get; init; } = string.Empty;

    /// <summary>정규화한 라우트(소문자, 앞 슬래시 고정, 라우트 제약 제거). ID와 비교에 쓴다.</summary>
    public string NormalizedRoute { get; init; } = string.Empty;

    public EndpointSource Source { get; init; } = EndpointSource.Controller;

    public HandlerKind HandlerKind { get; init; } = HandlerKind.Method;

    /// <summary>라우트·핸들러를 상수로 확정했으면 Certain, 일부를 추정했으면 Estimated다.</summary>
    public CallConfidence Confidence { get; init; } = CallConfidence.Certain;

    /// <summary>등록식이 있는 파일(분석 루트 기준 상대 경로). 컨트롤러는 메서드 선언 파일이다.</summary>
    public string RegistrationFile { get; init; } = string.Empty;

    public int RegistrationLine { get; init; }

    /// <summary>핸들러 표시 이름. 람다는 "GET /orders 핸들러"처럼 읽을 수 있는 이름을 넣는다.</summary>
    public string HandlerDisplayName { get; init; } = string.Empty;

    /// <summary>프로젝트 구분 키(어셈블리 이름). 같은 라우트가 여러 프로젝트에 있을 때 구분한다.</summary>
    public string ProjectKey { get; init; } = string.Empty;

    /// <summary>추정으로 남긴 부분이나 지원하지 않는 패턴에 대한 설명.</summary>
    public IReadOnlyList<string> Notes
    {
        get => notes;
        init => notes = value ?? [];
    }

    /// <summary>
    /// <c>[ApiVersion("1.0")]</c>으로 선언한 버전. 라우트에 <c>{version:apiVersion}</c> 토큰이 있으면
    /// 버전마다 진입점이 하나씩 나오고, 없으면(헤더·쿼리 버전) 라우트는 같고 endpointId로만 구분된다.
    /// v7 이전 문서에는 없고, 없으면 빈 목록으로 읽는다.
    /// </summary>
    public IReadOnlyList<string> ApiVersions { get; init; } = [];
}

namespace TraceLens.Analyzer.Models;

/// <summary>실행 본문을 가진 단위의 모양. 람다·로컬 함수는 메서드 심볼 ID가 없어 구조 기반 ID를 쓴다.</summary>
public enum CallableKind
{
    Method,
    Constructor,
    PropertyAccessor,
    LocalFunction,
    Lambda
}

public sealed record GraphNode(
    string Id,
    NodeKind Kind,
    string DisplayName,
    string Namespace,
    string ClassId,
    string File,
    int StartLine,
    int EndLine)
{
    private readonly IReadOnlyList<string> parameterTypes = [];

    public MemberAccessibility Accessibility { get; init; } = MemberAccessibility.Public;

    /// <summary>표시용 반환 타입. 생성자는 빈 문자열이다.</summary>
    public string ReturnType { get; init; } = string.Empty;

    /// <summary>노드 id에 쓰는 것과 같은 전체 타입 이름 목록.</summary>
    public IReadOnlyList<string> ParameterTypes
    {
        get => parameterTypes;
        init => parameterTypes = value ?? [];
    }

    /// <summary>본문의 모양. v6 이하 문서를 읽으면 <see cref="CallableKind.Method"/>다.</summary>
    public CallableKind Callable { get; init; } = CallableKind.Method;

    /// <summary>
    /// 프로젝트·타입·메서드 종류·제네릭 인자 수·파라미터 타입과 ref 종류까지 담은 비교용 키다.
    /// <see cref="Id"/>는 프로젝트가 다른 동명 타입을 구분하지 못하므로 스냅샷 비교는 이 키를 쓴다.
    /// </summary>
    public string StableKey { get; init; } = string.Empty;

    /// <summary>시그니처(접근제한자·반환 타입·이름·파라미터)의 지문. 본문이 그대로여도 시그니처가 바뀌면 달라진다.</summary>
    public string SignatureFingerprint { get; init; } = string.Empty;

    /// <summary>
    /// 본문의 지문이다. 공백·주석(trivia)을 뺀 토큰 종류와 텍스트의 열을 해시한다.
    /// 의미적 동등성의 증명이 아니라 "본문이 바뀌었는지"의 판단 근거일 뿐이다.
    /// </summary>
    public string BodyFingerprint { get; init; } = string.Empty;
}

public enum NodeKind
{
    Controller,
    Service,
    Repository,
    Validator,
    Infra,
    Db,
    Method,
    Interface
}

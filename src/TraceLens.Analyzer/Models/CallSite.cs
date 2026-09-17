namespace TraceLens.Analyzer.Models;

/// <summary>호출 한 지점을 어떤 근거로 대상 메서드에 이었는지.</summary>
public enum CallResolution
{
    /// <summary>선언된 심볼이 그대로 솔루션 안 구현이라 추가 해석이 필요 없었다.</summary>
    Direct,

    /// <summary>인터페이스 호출을 DI 등록(<c>AddScoped&lt;I, Impl&gt;</c> 등)으로 구현체까지 이었다.</summary>
    DiRegistration,

    /// <summary>DI 등록을 찾지 못했지만 솔루션 안 구현체가 하나뿐이라 그 구현체로 이었다.</summary>
    SingleImplementation,

    /// <summary>후보가 여러 개라 확정하지 못했다. 엣지를 만들지 않는다.</summary>
    Ambiguous,

    /// <summary>심볼을 해석하지 못했다. 엣지를 만들지 않는다.</summary>
    Unresolved,

    /// <summary>솔루션 밖(프레임워크·패키지) 호출이다. 엣지를 만들지 않고 경계로만 센다.</summary>
    External
}

/// <summary>판정의 확실도. <see cref="CallResolution.SingleImplementation"/>처럼 추론이 섞인 근거는 <see cref="Estimated"/>다.</summary>
public enum CallConfidence
{
    Certain,
    Estimated
}

/// <summary>
/// 호출 엣지의 근거가 되는 실제 호출 위치.
/// 엣지 자체는 <c>From/To</c>로 중복 제거하므로, 같은 메서드 쌍을 여러 번 호출하는 경우를 잃지 않도록 여기에 따로 남긴다.
/// </summary>
/// <param name="From">호출하는 쪽 노드 ID.</param>
/// <param name="To">해석한 대상 노드 ID. 확정하지 못했으면 빈 문자열이다.</param>
/// <param name="DeclaredTarget">소스에 적힌 대로의 대상(인터페이스 메서드 등). 해석 전 모습이다.</param>
public sealed record CallSite(
    string From,
    string To,
    string DeclaredTarget,
    string File,
    int Line,
    CallResolution Resolution,
    CallConfidence Confidence)
{
    /// <summary>확정하지 못한 이유나 후보 수처럼 사람이 읽을 보충 설명.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>
    /// 람다 본문 안에서 나온 호출인지. 이 호출은 람다를 받는 쪽이 실제로 부를 때만 실행되므로
    /// <see cref="Confidence"/>가 항상 <see cref="CallConfidence.Estimated"/>다.
    /// v7 이전에 저장된 문서에는 이 필드가 없고, 없으면 false로 읽는다.
    /// </summary>
    public bool ViaLambda { get; init; }
}

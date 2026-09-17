namespace TraceLens.Analyzer.Models;

/// <summary>
/// 호출 엣지 하나. 같은 <c>From/To</c> 쌍은 하나로 합치고, 근거는 <see cref="CallSite"/>에 따로 남긴다.
/// </summary>
public sealed record GraphEdge(string From, string To)
{
    /// <summary>
    /// 이 엣지의 확실도. 근거 중 가장 높은 것을 따른다 — 람다 본문 안의 호출만 있으면 추정이고,
    /// 직접 호출이 하나라도 있으면 확실이다. v7 이전 문서에는 없고, 없으면 확실로 읽는다.
    /// </summary>
    public CallConfidence Confidence { get; init; } = CallConfidence.Certain;
}

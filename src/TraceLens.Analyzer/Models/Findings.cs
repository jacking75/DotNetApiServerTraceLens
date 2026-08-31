namespace TraceLens.Analyzer.Models;

public enum FindingSeverity
{
    Critical,
    Warning,
    Info
}

/// <summary>판정의 확신도. 추정은 화면에서 접을 수 있게 구분해 둔다.</summary>
public enum FindingConfidence
{
    /// <summary>심볼·구문만으로 확정할 수 있다.</summary>
    Certain,

    /// <summary>런타임 값이나 이름 규칙에 기대는 판단이라 오탐이 있을 수 있다.</summary>
    Estimated
}

/// <param name="Tone">근거 한 줄의 성격. <c>ok</c>는 확인된 사실, <c>bad</c>는 문제의 근거, 빈 값은 중립이다.</param>
public sealed record FindingEvidence(string Tone, string Head, string Detail, string Note);

/// <summary>
/// 규칙 하나가 잡아낸 항목. DB·캐시 보기와 미들웨어 보기가 같은 모양을 쓴다.
/// </summary>
/// <param name="PathId">보상 처리 보기의 쓰기 경로 id. 그 보기와 이어질 때만 채운다.</param>
/// <param name="StepId">미들웨어 단계 id. 미들웨어 규칙일 때만 채운다.</param>
/// <param name="Target">테이블 이름이나 캐시 키 패턴처럼 화면에서 되짚을 대상. 없으면 null.</param>
public sealed record Finding(
    string Id,
    string RuleId,
    FindingSeverity Severity,
    FindingConfidence Confidence,
    string What,
    string Why,
    string Fix,
    string Symbol,
    string File,
    int Line,
    string? NodeId,
    string? PathId,
    string? StepId,
    string? Target,
    IReadOnlyList<FindingEvidence> Evidence)
{
    private readonly IReadOnlyList<FindingEvidence> evidence = Evidence ?? [];

    public IReadOnlyList<FindingEvidence> Evidence
    {
        get => evidence;
        init => evidence = value ?? [];
    }
}

/// <summary>
/// 규칙 카탈로그 한 줄. 아직 구현하지 않은 규칙도 <see cref="Enabled"/>가 false인 채로 함께 실어
/// "무엇을 검사하고 무엇을 검사하지 않는지"를 화면에서 그대로 보여준다.
/// </summary>
/// <param name="ShardOnly">샤딩을 쓰는 솔루션에서만 켜지는 규칙인지.</param>
/// <param name="Enabled">이번 분석에서 실제로 검사했는지.</param>
public sealed record RuleInfo(
    string Id,
    string Group,
    string Name,
    FindingSeverity Severity,
    FindingConfidence Judge,
    string Description,
    bool ShardOnly,
    bool Enabled);

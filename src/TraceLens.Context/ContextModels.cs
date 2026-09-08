namespace TraceLens.Context;

/// <summary>문맥을 만들 때 무엇을 골랐는지.</summary>
public enum SelectionKind
{
    Node,
    Endpoint,
    Class,
    Namespace,
    Finding,
    WritePath,
    MiddlewareStep,
    StartupStep
}

/// <summary>작업 목적. 무엇을 우선 담을지와 안내 문구가 달라진다.</summary>
public enum ContextIntent
{
    /// <summary>처음 보는 API를 이해한다.</summary>
    Explain,

    /// <summary>기능 추가·수정을 계획한다.</summary>
    PlanChange,

    /// <summary>AI가 만든 수정을 검토한다.</summary>
    ReviewChange,

    /// <summary>DB·캐시 불일치를 조사한다.</summary>
    InvestigateData,

    /// <summary>사람에게 공유할 요약을 만든다(소스와 절대 경로를 넣지 않는다).</summary>
    Share
}

/// <summary>길이 예산. 모델별 tokenizer가 없으므로 문자·노드·줄 수의 결정적 상한으로 구현한다.</summary>
public enum ContextBudget
{
    Short,
    Balanced,
    Detailed
}

public sealed record ContextSelection(SelectionKind Kind, string Id)
{
    public string DisplayName { get; init; } = string.Empty;
}

public sealed record ContextRequest(
    ContextSelection Selection,
    ContextIntent Intent = ContextIntent.Explain,
    ContextBudget Budget = ContextBudget.Balanced)
{
    /// <summary>사용자가 실제로 물어보려는 것. 문맥 맨 앞에 그대로 들어간다.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>호출자가 기대하는 스냅샷. 다르면 <c>SNAPSHOT_MISMATCH</c>로 거절한다.</summary>
    public string? ExpectedSnapshotId { get; init; }

    /// <summary>소스 본문을 넣을지. 끄면 위치·시그니처만 넣는다.</summary>
    public bool IncludeSource { get; init; } = true;

    /// <summary>문맥에서 뺄 파일 경로(접두사 일치). 비밀값 자동 탐지에 기대지 않고 수동 제외를 제공한다.</summary>
    public IReadOnlyList<string> ExcludeFiles { get; init; } = [];
}

public sealed record ContextFreshness(string Status, IReadOnlyList<string> Reasons)
{
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
}

/// <param name="Kind">callSite · declaration · boundary · finding · registration 중 하나다.</param>
public sealed record ContextEvidence(
    string Id,
    string Kind,
    string File,
    int StartLine,
    int EndLine,
    string Resolution,
    string Confidence,
    string Detail);

/// <param name="SourceVersion">snapshot이면 분석 당시와 같은 내용이고, current면 분석 이후 바뀐 현재 소스다.</param>
public sealed record ContextSourceSnippet(
    string EvidenceId,
    string File,
    int StartLine,
    int EndLine,
    string Content,
    bool Truncated,
    string SourceVersion)
{
    /// <summary>길이 제한으로 잘라낸 줄 범위. 무엇이 빠졌는지 분명히 하기 위해 남긴다.</summary>
    public IReadOnlyList<string> OmittedRanges { get; init; } = [];
}

public sealed record ContextPathStep(
    string NodeId,
    string DisplayName,
    string File,
    int StartLine,
    string Resolution,
    string Confidence);

public sealed record ContextPath(
    string EndpointId,
    string HttpMethod,
    string Route,
    IReadOnlyList<ContextPathStep> Steps,
    bool Truncated);

public sealed record ContextNeighbor(string NodeId, string DisplayName, string File, int StartLine, string Kind);

public sealed record ContextFinding(
    string Id,
    string RuleId,
    string Severity,
    string Confidence,
    string What,
    string Why,
    string Fix,
    string Symbol,
    string File,
    int Line);

public sealed record ContextOmission(int Nodes, IReadOnlyList<string> SourceRanges, IReadOnlyList<string> Sections);

public sealed record ContextAnalysis(
    DateTimeOffset AnalyzedAt,
    string AnalysisScope,
    int LoadedProjects,
    int TotalProjects,
    int ProjectsWithCompileErrors,
    int InspectedCallSites,
    int ResolvedCallSites,
    double ResolutionRate);

/// <summary>
/// UI 미리보기·클립보드·인앱 실행·MCP가 모두 이 하나의 DTO에서 나온다.
/// 서로 다른 곳에서 문맥을 따로 조립하지 않기 위한 계약이다.
/// </summary>
public sealed record ContextDocument(
    int ContextVersion,
    string SnapshotId,
    string InputFingerprint,
    ContextSelection Selection,
    string Intent,
    string Budget,
    ContextFreshness Freshness,
    string Summary,
    ContextAnalysis Analysis,
    IReadOnlyList<ContextEvidence> Evidence,
    IReadOnlyList<ContextSourceSnippet> SourceSnippets,
    IReadOnlyList<ContextPath> Paths,
    IReadOnlyList<ContextNeighbor> Callers,
    IReadOnlyList<ContextNeighbor> Callees,
    IReadOnlyList<ContextFinding> Findings,
    IReadOnlyList<ContextNeighbor> TestCandidates,
    IReadOnlyList<string> Limitations,
    bool Truncated,
    ContextOmission Omitted)
{
    public const int Version = 1;

    /// <summary>사용자가 적은 질문. 그대로 문맥 맨 앞에 들어간다.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>같은 DTO에서 만든 Markdown. 클립보드와 MCP가 같은 문자열을 쓴다.</summary>
    public string Markdown { get; init; } = string.Empty;

    /// <summary>Markdown의 문자 수.</summary>
    public int Characters => Markdown.Length;

    /// <summary>대략적인 토큰 수. 모델별 tokenizer가 아니므로 "예상"이라고만 쓴다.</summary>
    public int EstimatedTokens => (Markdown.Length + 3) / 4;

    /// <summary>문맥에 실제 소스 본문이 들어갔는지. 외부 AI로 보내기 전에 사용자가 확인할 수 있어야 한다.</summary>
    public bool ContainsSource => SourceSnippets.Count > 0;

    /// <summary>문맥에 등장하는 파일 목록.</summary>
    public IReadOnlyList<string> Files => Evidence
        .Select(item => item.File)
        .Concat(SourceSnippets.Select(item => item.File))
        .Where(file => !string.IsNullOrEmpty(file))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(file => file, StringComparer.Ordinal)
        .ToArray();
}

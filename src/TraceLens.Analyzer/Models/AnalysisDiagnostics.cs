namespace TraceLens.Analyzer.Models;

/// <param name="SampleErrors">프로젝트마다 최대 5건만 남긴다. 전체 오류를 보관하려는 목적이 아니다.</param>
public sealed record AnalyzedProject(
    string Name,
    string Path,
    bool Loaded,
    int ErrorCount,
    IReadOnlyList<string> SampleErrors)
{
    /// <summary>
    /// 테스트 프로젝트로 보이는지(xunit·nunit·MSTest 참조 또는 <c>IsTestProject</c>).
    /// 제외하지는 않는다 — 사용자가 테스트 코드의 호출도 보고 싶을 수 있다.
    /// 유일 구현체 판정에서 테스트 더블을 후보에서 빼는 데만 쓴다(T59).
    /// </summary>
    public bool IsTest { get; init; }
}

/// <summary>
/// 솔루션 밖(프레임워크·패키지) 호출을 네임스페이스로 묶은 집계다.
/// 엣지를 늘리지 않고 "무엇을 보고도 해석하지 않았는지"만 남긴다. 개별 호출 지점은 저장하지 않는다.
/// </summary>
/// <param name="SampleType">그 네임스페이스에서 본 타입 하나. 무엇을 가리키는지 알아보게 한다.</param>
public sealed record ExternalCallGroup(string Namespace, int Count, string SampleType);

/// <summary>
/// HTTP 밖 실행 단위 후보. 진입점·엣지를 만들지 않고 종류별 개수와 이름만 남긴다.
/// "변경 영향에 도달하는 진입점이 없다"가 무엇을 세지 않은 결과인지 알 수 있게 한다.
/// </summary>
/// <param name="Samples">최대 3개까지의 타입 이름.</param>
public sealed record EntryPointCandidateGroup(string Kind, int Count, IReadOnlyList<string> Samples);

/// <summary>
/// 호출 지점 판정의 개수. 분모인 <paramref name="Inspected"/>는 <b>솔루션 안 대상</b>을 부른 호출 지점만 센다.
/// 프레임워크·패키지 호출(<paramref name="External"/>)은 애초에 그래프의 대상이 아니라 분모에서 뺀다.
/// </summary>
public sealed record CallResolutionStats(
    int Inspected,
    int Resolved,
    int Direct,
    int DiRegistration,
    int SingleImplementation,
    int Ambiguous,
    int Unresolved,
    int External)
{
    public static CallResolutionStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>솔루션 밖 호출의 네임스페이스별 상위 집계(건수 내림차순). 해석률에는 영향을 주지 않는다.</summary>
    public IReadOnlyList<ExternalCallGroup> ExternalByNamespace { get; init; } = [];

    /// <summary>해석률 = 해석한 지원 대상 호출 지점 / 검사한 지원 대상 호출 지점. 호출 "정확도"와는 다른 값이다.</summary>
    public double ResolutionRate => Inspected == 0 ? 1d : Math.Round((double)Resolved / Inspected, 4);
}

/// <summary>
/// 이번 분석에서 무엇을 보고 무엇을 못 봤는지 숫자와 이유로 남긴다.
/// "정확도 n%" 같은 분모 없는 표현 대신 이 값만 화면과 문맥에 쓴다.
/// </summary>
public sealed record AnalysisDiagnostics(
    int TotalProjects,
    int LoadedProjects,
    int ProjectsWithCompileErrors,
    IReadOnlyList<AnalyzedProject> Projects,
    CallResolutionStats Calls,
    IReadOnlyList<string> ExcludedNamespacePrefixes,
    int MaxCallDepth,
    bool DepthLimitReached,
    string AnalysisScope,
    IReadOnlyList<string> Limitations)
{
    public static AnalysisDiagnostics Empty { get; } = new(
        0, 0, 0, [], CallResolutionStats.Empty, [], 0, false, "solution", []);

    /// <summary>분석 시작과 끝 사이에 입력 파일이 바뀌었는지. 자동으로 다시 분석하지는 않는다.</summary>
    public bool ChangedDuringAnalysis { get; init; }

    public IReadOnlyList<string> ChangedDuringAnalysisFiles { get; init; } = [];

    /// <summary>진입점을 하나도 찾지 못했을 때 그것이 "정상"이 아님을 알리는 안내.</summary>
    public IReadOnlyList<string> EntryPointNotes { get; init; } = [];

    /// <summary>HTTP 밖 실행 단위 후보(종류별). 진입점 수·엣지 수에는 영향을 주지 않는다.</summary>
    public IReadOnlyList<EntryPointCandidateGroup> EntryPointCandidates { get; init; } = [];
}

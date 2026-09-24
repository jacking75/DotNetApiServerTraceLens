using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraceLens.Analyzer.Models;

/// <param name="SolutionPath">분석 대상으로 고른 파일의 전체 경로(.sln, .slnx 또는 .csproj).</param>
public sealed record GraphDocument(
    int SchemaVersion,
    string SolutionPath,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<EntryPoint> EntryPoints,
    IReadOnlyList<GraphClass> Classes,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyDictionary<string, string> FileHashes)
{
    /// <summary>
    /// v4에서 <c>classes</c>에 타입 메타·상속·구현·멤버가, <c>nodes</c>에 접근제한자·반환타입·파라미터 타입이 들어갔다.
    /// v5에서 Program.cs를 분석한 <see cref="StartupFlow"/>(DI 등록·미들웨어 파이프라인)가 추가됐다.
    /// v6에서 <see cref="DataAccess"/>(DB·캐시 접근과 보상 처리)와 <see cref="Middleware"/>(미들웨어 파이프라인)가 추가됐다.
    /// v7에서 <see cref="SnapshotId"/>·<see cref="InputFingerprint"/>·<see cref="Manifest"/>·<see cref="Diagnostics"/>·
    /// <see cref="CallSites"/>와 진입점의 <c>endpointId</c>, 노드의 지문·안정 키가 추가됐다.
    /// v6 이하로 저장된 문서를 읽으면 새 필드는 각 모델의 기본값(빈 목록·null·Class)으로 채워진다.
    /// </summary>
    public const int CurrentSchemaVersion = 7;

    /// <summary>이 버전보다 큰 schema는 읽지 않고 명시적으로 거절한다.</summary>
    public const int MaxSupportedSchemaVersion = CurrentSchemaVersion;

    private readonly IReadOnlyList<GraphClass> classes = Classes ?? [];
    private readonly IReadOnlyList<CallSite> callSites = [];
    private readonly string? rootDirectory;

    /// <summary>스키마 v1으로 저장된 그래프에는 classes가 없으므로 null 대신 빈 목록으로 읽는다.</summary>
    public IReadOnlyList<GraphClass> Classes
    {
        get => classes;
        init => classes = value ?? [];
    }

    /// <summary>
    /// 노드/클래스의 상대 경로, 신선도 확인, <c>.tracelens</c> 출력의 기준 디렉터리.
    /// .sln을 분석하면 솔루션 디렉터리이고, .csproj를 분석하면 그 프로젝트와 참조 프로젝트를 모두 담는 상위 디렉터리다.
    /// 스키마 v2 이하로 저장된 그래프에는 이 값이 없으므로 <see cref="SolutionPath"/>의 디렉터리로 되돌린다.
    /// </summary>
    public string RootDirectory
    {
        get => string.IsNullOrEmpty(rootDirectory) ? Path.GetDirectoryName(SolutionPath) ?? string.Empty : rootDirectory;
        init => rootDirectory = value;
    }

    /// <summary>
    /// Program.cs를 분석한 시작 흐름(DI 등록·미들웨어 파이프라인). 찾지 못했거나 v4 이하 문서면 null이다.
    /// 실행 가능한 프로젝트가 여럿이면 <see cref="StartupFlows"/>의 첫 항목과 같다(호환용).
    /// </summary>
    public StartupFlowDocument? StartupFlow { get; init; }

    /// <summary>
    /// 프로젝트별 시작 흐름. 실행 가능한 프로젝트가 여럿인 솔루션에서 두 번째 서비스의 등록이 빠지지 않게 한다.
    /// v7 이전 문서에는 없고, 없으면 <see cref="StartupFlow"/> 하나만 있는 것으로 읽는다.
    /// </summary>
    public IReadOnlyList<StartupFlowDocument> StartupFlows { get; init; } = [];

    /// <summary>DB·캐시 접근과 보상 처리 점검. 설정에서 껐거나 v5 이하 문서면 null이다.</summary>
    public DataAccessDocument? DataAccess { get; init; }

    /// <summary>
    /// 미들웨어 파이프라인. Program.cs를 찾지 못했거나 v5 이하 문서면 null이다.
    /// 실행 가능한 프로젝트가 여럿이면 <see cref="MiddlewarePipelines"/>의 첫 항목과 같다(호환용).
    /// </summary>
    public MiddlewarePipelineDocument? Middleware { get; init; }

    /// <summary>
    /// 프로젝트별 미들웨어 파이프라인. v7 이전 문서에는 없고, 없으면 <see cref="Middleware"/> 하나만 있는 것으로 읽는다.
    /// </summary>
    public IReadOnlyList<MiddlewarePipelineDocument> MiddlewarePipelines { get; init; } = [];

    /// <summary>
    /// 이 분석 실행 하나를 가리키는 ID다. 같은 코드를 다시 분석하면 새 값이 나온다.
    /// UI·API·MCP가 같은 분석을 보고 있는지 확인하는 데 쓴다. v6 이하 문서면 빈 문자열이다.
    /// </summary>
    public string SnapshotId { get; init; } = string.Empty;

    /// <summary>
    /// 분석 입력(소스 해시 + 설정 해시 + 분석기 버전)만으로 만든 지문이다.
    /// 같은 입력이면 몇 번을 분석해도 같은 값이라 "내용이 같은 분석"을 알아볼 수 있다.
    /// </summary>
    public string InputFingerprint { get; init; } = string.Empty;

    /// <summary>이 분석이 무엇을 읽었는지. v6 이하 문서면 null이다.</summary>
    public AnalysisManifest? Manifest { get; init; }

    /// <summary>얼마나 보고 무엇을 못 봤는지. v6 이하 문서면 null이다.</summary>
    public AnalysisDiagnostics? Diagnostics { get; init; }

    /// <summary>엣지의 근거가 되는 실제 호출 위치. 설정에서 껐거나 v6 이하 문서면 빈 목록이다.</summary>
    public IReadOnlyList<CallSite> CallSites
    {
        get => callSites;
        init => callSites = value ?? [];
    }

    /// <summary>분석을 만든 TraceLens 버전. 스냅샷 비교의 전제 조건 확인에 쓴다.</summary>
    public string AnalyzerVersion { get; init; } = string.Empty;

    /// <summary>DB·미들웨어 규칙 카탈로그의 버전. 규칙이 바뀌면 점검 비교 조건이 달라진다.</summary>
    public string RulesVersion { get; init; } = string.Empty;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

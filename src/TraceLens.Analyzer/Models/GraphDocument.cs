using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraceLens.Analyzer.Models;

/// <param name="SolutionPath">분석 대상으로 고른 파일의 전체 경로(.sln 또는 .csproj).</param>
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
    /// v3 이하로 저장된 문서를 읽으면 새 필드는 각 모델의 기본값(빈 목록·null·Class)으로 채워진다.
    /// </summary>
    public const int CurrentSchemaVersion = 5;

    private readonly IReadOnlyList<GraphClass> classes = Classes ?? [];
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

    /// <summary>Program.cs를 분석한 시작 흐름(DI 등록·미들웨어 파이프라인). 찾지 못했거나 v4 이하 문서면 null이다.</summary>
    public StartupFlowDocument? StartupFlow { get; init; }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed record EntryPoint(string NodeId, string HttpMethod, string Route);

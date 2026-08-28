using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraceLens.Analyzer.Models;

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
    public const int CurrentSchemaVersion = 2;

    private readonly IReadOnlyList<GraphClass> classes = Classes ?? [];

    /// <summary>스키마 v1으로 저장된 그래프에는 classes가 없으므로 null 대신 빈 목록으로 읽는다.</summary>
    public IReadOnlyList<GraphClass> Classes
    {
        get => classes;
        init => classes = value ?? [];
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed record EntryPoint(string NodeId, string HttpMethod, string Route);

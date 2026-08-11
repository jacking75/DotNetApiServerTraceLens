using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraceLens.Analyzer.Models;

public sealed record GraphDocument(
    int SchemaVersion,
    string SolutionPath,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<EntryPoint> EntryPoints,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyDictionary<string, string> FileHashes)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed record EntryPoint(string NodeId, string HttpMethod, string Route);

using System.Text.Json;
using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

/// <summary>그래프 JSON을 읽을 때 확인한 문제.</summary>
public sealed record GraphValidationIssue(string Code, string Message);

public sealed record GraphLoadResult(GraphDocument? Graph, IReadOnlyList<GraphValidationIssue> Issues)
{
    public bool Ok => Graph is not null && Issues.All(issue => !issue.Code.StartsWith("E_", StringComparison.Ordinal));
}

/// <summary>
/// <c>graph.json</c>을 읽고 최소한의 무결성을 확인한다.
/// 지원하지 않는 미래 schema는 명시적으로 거절하고, 중복 ID·존재하지 않는 노드를 참조하는 엣지도 확인한다.
/// </summary>
public static class GraphJsonStore
{
    public static async Task<GraphLoadResult> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return new GraphLoadResult(null, [new GraphValidationIssue("E_NOT_FOUND", $"그래프 파일을 찾을 수 없다: {path}")]);
        }

        GraphDocument? graph;
        try
        {
            await using var stream = File.OpenRead(path);
            graph = await JsonSerializer.DeserializeAsync<GraphDocument>(stream, GraphDocument.JsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            return new GraphLoadResult(null, [new GraphValidationIssue("E_INVALID_JSON", exception.Message)]);
        }

        return Validate(graph);
    }

    public static GraphLoadResult Parse(string json)
    {
        try
        {
            return Validate(JsonSerializer.Deserialize<GraphDocument>(json, GraphDocument.JsonOptions));
        }
        catch (JsonException exception)
        {
            return new GraphLoadResult(null, [new GraphValidationIssue("E_INVALID_JSON", exception.Message)]);
        }
    }

    public static GraphLoadResult Validate(GraphDocument? graph)
    {
        var issues = new List<GraphValidationIssue>();
        if (graph is null)
        {
            return new GraphLoadResult(null, [new GraphValidationIssue("E_EMPTY", "분석 결과가 비어 있다.")]);
        }

        if (graph.SchemaVersion > GraphDocument.MaxSupportedSchemaVersion)
        {
            issues.Add(new GraphValidationIssue(
                "E_SCHEMA_TOO_NEW",
                $"schema v{graph.SchemaVersion}는 이 버전이 읽지 못한다(지원 v{GraphDocument.MaxSupportedSchemaVersion}까지). TraceLens를 업데이트해야 한다."));
            return new GraphLoadResult(null, issues);
        }

        if (string.IsNullOrWhiteSpace(graph.SolutionPath))
        {
            issues.Add(new GraphValidationIssue("E_NO_TARGET", "분석 결과 파일에 솔루션 경로 정보가 없다."));
            return new GraphLoadResult(null, issues);
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var duplicateNodes = 0;
        foreach (var node in graph.Nodes)
        {
            if (!nodeIds.Add(node.Id))
            {
                duplicateNodes++;
            }
        }

        if (duplicateNodes > 0)
        {
            issues.Add(new GraphValidationIssue("W_DUPLICATE_NODE", $"중복된 노드 ID가 {duplicateNodes}개 있다."));
        }

        var danglingEdges = graph.Edges.Count(edge => !nodeIds.Contains(edge.From) || !nodeIds.Contains(edge.To));
        if (danglingEdges > 0)
        {
            issues.Add(new GraphValidationIssue("W_DANGLING_EDGE", $"존재하지 않는 노드를 참조하는 엣지가 {danglingEdges}개 있다."));
        }

        var danglingEntryPoints = graph.EntryPoints.Count(item => !nodeIds.Contains(item.NodeId));
        if (danglingEntryPoints > 0)
        {
            issues.Add(new GraphValidationIssue(
                "W_DANGLING_ENTRYPOINT",
                $"핸들러 노드가 그래프에 없는 진입점이 {danglingEntryPoints}개 있다."));
        }

        if (graph.Nodes.Any(node => string.IsNullOrEmpty(node.File)) )
        {
            issues.Add(new GraphValidationIssue("W_NO_FILE", "파일 경로가 비어 있는 노드가 있다."));
        }

        return new GraphLoadResult(graph, issues);
    }

    /// <summary>임시 파일에 완전히 쓴 다음 검증하고 교체한다. 실패하면 이전 파일이 그대로 남는다.</summary>
    public static async Task WriteAtomicallyAsync(
        string path,
        GraphDocument graph,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(graph, GraphDocument.JsonOptions), cancellationToken);
        try
        {
            var verify = await LoadAsync(temporaryPath, cancellationToken);
            if (!verify.Ok)
            {
                throw new InvalidDataException(
                    "새로 쓴 그래프 파일을 다시 읽지 못했다: " + string.Join(", ", verify.Issues.Select(issue => issue.Message)));
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 임시 파일을 못 지워도 저장 결과에는 영향이 없다.
            }

            throw;
        }
    }
}

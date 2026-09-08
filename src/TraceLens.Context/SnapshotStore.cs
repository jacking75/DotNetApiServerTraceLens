using System.Text.Json;
using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

/// <param name="Label">사용자가 붙인 이름. 비교 화면에서 "변경 전" 같은 표시로 쓴다.</param>
public sealed record SnapshotEntry(
    string SnapshotId,
    string InputFingerprint,
    DateTimeOffset AnalyzedAt,
    DateTimeOffset SavedAt,
    string Label,
    string SolutionPath,
    string AnalyzerVersion,
    string RulesVersion,
    string AnalysisScope,
    int NodeCount,
    int EdgeCount,
    int EntryPointCount,
    int FindingCount);

/// <summary>
/// 비교 기준으로 쓸 분석 결과를 <c>.tracelens/history/&lt;snapshotId&gt;/graph.json</c>에 보관한다.
/// 대상 루트는 그래프의 <see cref="GraphDocument.RootDirectory"/>를 따르며 TraceLens 본체 루트와 섞이지 않는다.
/// 사용자가 고른 기준만 보관하고 무제한으로 쌓지 않는다.
/// </summary>
public sealed class SnapshotStore
{
    /// <summary>보관 상한. 넘으면 정리를 안내한다(자동으로 지우지 않는다).</summary>
    public const int SoftLimit = 20;

    private static readonly JsonSerializerOptions IndexJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string GetHistoryDirectory(GraphDocument graph) =>
        Path.Combine(graph.RootDirectory, ".tracelens", "history");

    public async Task<SnapshotEntry> SaveAsync(
        GraphDocument graph,
        string label,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(graph.RootDirectory) || !Directory.Exists(graph.RootDirectory))
        {
            throw new InvalidOperationException("분석 루트를 확인할 수 없어 기준을 저장할 수 없다.");
        }

        var snapshotId = string.IsNullOrEmpty(graph.SnapshotId)
            ? "legacy-" + graph.AnalyzedAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")
            : graph.SnapshotId;
        var directory = Path.Combine(GetHistoryDirectory(graph), Sanitize(snapshotId));
        Directory.CreateDirectory(directory);
        await GraphJsonStore.WriteAtomicallyAsync(Path.Combine(directory, "graph.json"), graph, cancellationToken);

        var entry = new SnapshotEntry(
            snapshotId,
            graph.InputFingerprint,
            graph.AnalyzedAt,
            DateTimeOffset.Now,
            string.IsNullOrWhiteSpace(label) ? "변경 전" : label.Trim(),
            graph.SolutionPath,
            graph.AnalyzerVersion,
            graph.RulesVersion,
            graph.Diagnostics?.AnalysisScope ?? "unknown",
            graph.Nodes.Count,
            graph.Edges.Count,
            graph.EntryPoints.Count,
            (graph.DataAccess?.Findings.Count ?? 0) + (graph.Middleware?.Findings.Count ?? 0));
        await File.WriteAllTextAsync(
            Path.Combine(directory, "snapshot.json"),
            JsonSerializer.Serialize(entry, IndexJsonOptions),
            cancellationToken);
        return entry;
    }

    public async Task<IReadOnlyList<SnapshotEntry>> ListAsync(
        GraphDocument graph,
        CancellationToken cancellationToken = default)
    {
        var historyDirectory = GetHistoryDirectory(graph);
        if (!Directory.Exists(historyDirectory))
        {
            return [];
        }

        var result = new List<SnapshotEntry>();
        foreach (var directory in Directory.EnumerateDirectories(historyDirectory))
        {
            var metadataPath = Path.Combine(directory, "snapshot.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<SnapshotEntry>(
                    await File.ReadAllTextAsync(metadataPath, cancellationToken),
                    IndexJsonOptions);
                if (entry is not null)
                {
                    result.Add(entry);
                }
            }
            catch (JsonException)
            {
                // 깨진 보관 항목은 목록에서 조용히 뺀다. 지우지는 않는다.
            }
        }

        return result.OrderByDescending(item => item.SavedAt).ToArray();
    }

    public async Task<GraphLoadResult> LoadAsync(
        GraphDocument graph,
        string snapshotId,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetHistoryDirectory(graph), Sanitize(snapshotId), "graph.json");
        return await GraphJsonStore.LoadAsync(path, cancellationToken);
    }

    public bool Delete(GraphDocument graph, string snapshotId)
    {
        var directory = Path.Combine(GetHistoryDirectory(graph), Sanitize(snapshotId));
        if (!Directory.Exists(directory))
        {
            return false;
        }

        Directory.Delete(directory, recursive: true);
        return true;
    }

    /// <summary>임의 경로를 받지 않는다. snapshotId를 디렉터리 이름으로 쓸 수 있는 형태로만 남긴다.</summary>
    private static string Sanitize(string snapshotId)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            throw new ArgumentException("snapshotId가 필요하다.", nameof(snapshotId));
        }

        var cleaned = new string(snapshotId
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .ToArray());
        if (cleaned.Length == 0 || cleaned.All(character => character == '.'))
        {
            throw new ArgumentException("snapshotId에 쓸 수 있는 문자가 없다.", nameof(snapshotId));
        }

        return cleaned;
    }
}

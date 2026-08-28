using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

public interface ITraceLensAnalyzer
{
    Task<AnalysisResult> AnalyzeAsync(string solutionPath, CancellationToken cancellationToken = default);
}

public sealed class TraceLensAnalyzer(
    SolutionLoader solutionLoader,
    EntryPointScanner entryPointScanner,
    DiResolver diResolver,
    SolutionScanner solutionScanner,
    CallGraphWalker callGraphWalker,
    SnapshotHasher snapshotHasher,
    ILogger<TraceLensAnalyzer> logger) : ITraceLensAnalyzer
{
    public async Task<AnalysisResult> AnalyzeAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var fullSolutionPath = Path.GetFullPath(solutionPath);
        var solutionDirectory = Path.GetDirectoryName(fullSolutionPath)
            ?? throw new ArgumentException("솔루션 디렉터리를 확인할 수 없다.", nameof(solutionPath));
        var config = await TraceLensConfig.LoadAsync(solutionDirectory, cancellationToken);

        await using var loaded = await solutionLoader.LoadAsync(fullSolutionPath, cancellationToken);
        await diResolver.BuildAsync(loaded, cancellationToken);
        var entryPoints = await entryPointScanner.ScanAsync(loaded, config, cancellationToken);
        logger.LogInformation("진입점 {Count}개를 찾았다.", entryPoints.Count);

        var isSolutionScope = config.AnalysisScope == AnalysisScope.Solution;
        var scan = await solutionScanner.ScanAsync(
            loaded,
            config,
            solutionDirectory,
            isSolutionScope,
            cancellationToken);
        var (nodes, edges) = isSolutionScope
            ? (scan.Nodes, scan.Edges)
            : await WalkFromEntryPointsAsync(loaded, entryPoints, config, solutionDirectory, cancellationToken);

        var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var reachableEntryPoints = entryPoints
            .Select(item => item.EntryPoint)
            .Where(entryPoint => nodeIds.Contains(entryPoint.NodeId))
            .ToArray();
        var fileHashes = await snapshotHasher.ComputeAsync(
            solutionDirectory,
            nodes.Select(node => node.File).Concat(scan.Classes.Select(item => item.File)),
            cancellationToken);

        var document = new GraphDocument(
            GraphDocument.CurrentSchemaVersion,
            fullSolutionPath,
            DateTimeOffset.Now,
            reachableEntryPoints,
            scan.Classes,
            nodes,
            edges,
            fileHashes);
        var outputDirectory = Path.Combine(solutionDirectory, ".tracelens");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "graph.json");
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(document, GraphDocument.JsonOptions),
            cancellationToken);

        stopwatch.Stop();
        return new AnalysisResult(document, stopwatch.ElapsedMilliseconds, outputPath);
    }

    private async Task<(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges)> WalkFromEntryPointsAsync(
        LoadedSolution loaded,
        IReadOnlyList<ScannedEntryPoint> entryPoints,
        TraceLensConfig config,
        string solutionDirectory,
        CancellationToken cancellationToken)
    {
        var graph = await callGraphWalker.WalkAsync(loaded, entryPoints, config, solutionDirectory, cancellationToken);
        return (graph.Nodes, graph.Edges);
    }
}

public sealed record AnalysisResult(GraphDocument Graph, long ElapsedMs, string OutputPath);

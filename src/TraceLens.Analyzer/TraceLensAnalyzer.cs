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
        var graph = await callGraphWalker.WalkAsync(
            loaded,
            entryPoints,
            config,
            solutionDirectory,
            cancellationToken);
        var fileHashes = await snapshotHasher.ComputeAsync(
            solutionDirectory,
            graph.Nodes.Select(node => node.File),
            cancellationToken);

        var document = new GraphDocument(
            1,
            fullSolutionPath,
            DateTimeOffset.Now,
            entryPoints.Select(item => item.EntryPoint).ToArray(),
            graph.Nodes,
            graph.Edges,
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
}

public sealed record AnalysisResult(GraphDocument Graph, long ElapsedMs, string OutputPath);

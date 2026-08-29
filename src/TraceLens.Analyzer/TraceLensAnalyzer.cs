using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

public interface ITraceLensAnalyzer
{
    /// <param name="targetPath">분석 대상 .sln 또는 .csproj 경로.</param>
    Task<AnalysisResult> AnalyzeAsync(string targetPath, CancellationToken cancellationToken = default);
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
    public async Task<AnalysisResult> AnalyzeAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var fullTargetPath = Path.GetFullPath(targetPath);
        var targetDirectory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new ArgumentException("분석 대상 디렉터리를 확인할 수 없다.", nameof(targetPath));

        await using var loaded = await solutionLoader.LoadAsync(fullTargetPath, cancellationToken);
        var solutionDirectory = ResolveRootDirectory(targetDirectory, loaded);
        if (!string.Equals(solutionDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("참조 프로젝트까지 포함하도록 분석 루트를 {Root}로 잡았다.", solutionDirectory);
        }

        var config = await TraceLensConfig.LoadAsync(solutionDirectory, cancellationToken);
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
            fullTargetPath,
            DateTimeOffset.Now,
            reachableEntryPoints,
            scan.Classes,
            nodes,
            edges,
            fileHashes)
        {
            RootDirectory = solutionDirectory
        };
        var outputDirectory = Path.Combine(solutionDirectory, ".tracelens");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "graph.json");
        var json = JsonSerializer.Serialize(document, GraphDocument.JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        // members가 들어가면서 커진 만큼, 언제 includeMembers를 꺼야 할지 판단할 수 있게 크기를 남긴다.
        var sizeKb = json.Length / 1024;
        if (sizeKb > GraphSizeWarningKb)
        {
            logger.LogWarning(
                "graph.json이 {SizeKb}KB다. 화면이 느리면 tracelens.config.json의 includeMembers를 false로 두면 줄어든다.",
                sizeKb);
        }
        else
        {
            logger.LogInformation("graph.json {SizeKb}KB · 멤버 {MemberCount}개(수집 {MemberElapsedMs}ms).", sizeKb, scan.MemberCount, scan.MemberElapsedMs);
        }

        stopwatch.Stop();
        return new AnalysisResult(
            document,
            stopwatch.ElapsedMilliseconds,
            outputPath,
            scan.MemberCount,
            scan.MemberElapsedMs,
            json.Length);
    }

    /// <summary>이 크기를 넘으면 <c>includeMembers</c>를 끄라고 안내한다.</summary>
    private const int GraphSizeWarningKb = 500;

    /// <summary>
    /// 상대 경로와 출력의 기준이 될 디렉터리를 정한다.
    /// .sln이면 솔루션 디렉터리 그대로이고, .csproj면 참조 프로젝트까지 담도록 공통 상위 디렉터리로 올라간다.
    /// 드라이브 루트까지 올라가는 경우에는 범위가 지나치게 넓어지므로 대상 디렉터리를 그대로 쓴다.
    /// </summary>
    private static string ResolveRootDirectory(string targetDirectory, LoadedSolution loaded)
    {
        var root = targetDirectory;
        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            var projectDirectory = Path.GetDirectoryName(project.FilePath);
            if (string.IsNullOrEmpty(projectDirectory))
            {
                continue;
            }

            var common = FindCommonAncestor(root, Path.GetFullPath(projectDirectory));
            if (common is null || string.Equals(common, Path.GetPathRoot(common), StringComparison.OrdinalIgnoreCase))
            {
                return targetDirectory;
            }

            root = common;
        }

        return root;
    }

    /// <summary>두 디렉터리의 공통 상위 디렉터리를 돌려주고, 드라이브가 달라 공통 조상이 없으면 null을 돌려준다.</summary>
    private static string? FindCommonAncestor(string left, string right)
    {
        var current = new DirectoryInfo(left);
        while (current is not null)
        {
            var relative = Path.GetRelativePath(current.FullName, right);
            if (!Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
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

/// <param name="MemberCount">클래스 다이어그램용으로 모은 멤버 수.</param>
/// <param name="MemberElapsedMs">멤버 수집에만 쓴 시간(ms). 전체 <paramref name="ElapsedMs"/> 안에 포함된다.</param>
/// <param name="GraphSizeBytes">저장한 <c>graph.json</c>의 크기.</param>
public sealed record AnalysisResult(
    GraphDocument Graph,
    long ElapsedMs,
    string OutputPath,
    int MemberCount,
    long MemberElapsedMs,
    int GraphSizeBytes);

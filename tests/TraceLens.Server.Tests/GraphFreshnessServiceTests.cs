using System.Security.Cryptography;
using TraceLens.Analyzer.Models;
using TraceLens.Analyzer;
using TraceLens.Context;
using TraceLens.Server;
using Xunit;

namespace TraceLens.Server.Tests;

public sealed class GraphFreshnessServiceTests
{
    /// <summary>
    /// 새 신선도 입력(manifest)이 없는 옛 그래프는 바뀐 파일이 없어도 <c>fresh</c>라고 단정하지 않는다.
    /// 새 파일과 설정 변경을 확인할 방법이 없기 때문이다.
    /// </summary>
    [Fact]
    public async Task CheckAsync_ReturnsUnknown_ForLegacyGraphWithoutManifest()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        var service = new GraphFreshnessService(new GraphFreshnessEvaluator());

        var result = await service.CheckAsync(fixture.Graph);

        Assert.Equal(FreshnessStatus.Unknown, result.Status);
        Assert.Empty(result.ChangedFiles);
        Assert.Contains(result.Reasons, reason => reason.Contains("manifest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_ReturnsFresh_WhenManifestMatchesDisk()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        var service = new GraphFreshnessService(new GraphFreshnessEvaluator());

        var result = await service.CheckAsync(fixture.GraphWithManifest);

        Assert.Equal(FreshnessStatus.Fresh, result.Status);
        Assert.Empty(result.ChangedFiles);
    }

    /// <summary>기존 파일이 그대로여도 프로젝트 아래에 새 .cs가 생기면 stale이어야 한다.</summary>
    [Fact]
    public async Task CheckAsync_ReturnsStale_WhenNewSourceFileAppears()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        var service = new GraphFreshnessService(new GraphFreshnessEvaluator());

        await File.WriteAllTextAsync(Path.Combine(fixture.RootDirectory, "Services", "NewService.cs"), "새 파일");
        var result = await service.CheckAsync(fixture.GraphWithManifest);

        Assert.Equal(FreshnessStatus.Stale, result.Status);
        Assert.Contains(result.Changes, change => change.Kind == FreshnessChangeKind.Added);
    }

    /// <summary>설정 파일(tracelens.config.json)이 바뀌어도 stale이어야 한다.</summary>
    [Fact]
    public async Task CheckAsync_ReturnsStale_WhenConfigInputChanges()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        var service = new GraphFreshnessService(new GraphFreshnessEvaluator());

        await File.WriteAllTextAsync(Path.Combine(fixture.RootDirectory, "tracelens.config.json"), "{ \"maxCallDepth\": 3 }");
        var result = await service.CheckAsync(fixture.GraphWithManifest);

        Assert.Equal(FreshnessStatus.Stale, result.Status);
        Assert.Contains(result.Changes, change => change.Kind == FreshnessChangeKind.ConfigChanged);
    }

    [Fact]
    public async Task CheckAsync_ReturnsStale_WhenTrackedFileChangesOrDisappears()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        var service = new GraphFreshnessService(new GraphFreshnessEvaluator());

        await File.WriteAllTextAsync(fixture.SourcePath, "변경된 코드");
        var changed = await service.CheckAsync(fixture.Graph);
        File.Delete(fixture.SourcePath);
        var deleted = await service.CheckAsync(fixture.Graph);

        Assert.Equal(FreshnessStatus.Stale, changed.Status);
        Assert.Equal([fixture.RelativePath], changed.ChangedFiles);
        Assert.Equal(FreshnessStatus.Stale, deleted.Status);
        Assert.Equal([fixture.RelativePath], deleted.ChangedFiles);
    }

    private sealed class FreshnessFixture(
        string directory,
        string sourcePath,
        string relativePath,
        GraphDocument graph,
        GraphDocument graphWithManifest) : IAsyncDisposable
    {
        public string RootDirectory { get; } = directory;
        public string SourcePath { get; } = sourcePath;
        public string RelativePath { get; } = relativePath;
        public GraphDocument Graph { get; } = graph;
        public GraphDocument GraphWithManifest { get; } = graphWithManifest;

        public static async Task<FreshnessFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "tracelens-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var relativePath = "Services/OrderService.cs";
            var sourcePath = Path.Combine(directory, "Services", "OrderService.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            await File.WriteAllTextAsync(sourcePath, "원본 코드");
            await using var stream = File.OpenRead(sourcePath);
            var hash = await SHA256.HashDataAsync(stream);
            var graph = new GraphDocument(
                1,
                Path.Combine(directory, "Fixture.sln"),
                DateTimeOffset.Parse("2026-08-11T00:00:00+09:00"),
                [],
                [],
                [],
                [],
                new Dictionary<string, string> { [relativePath] = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant() });
            var configPath = Path.Combine(directory, "tracelens.config.json");
            await File.WriteAllTextAsync(configPath, "{ }");
            var graphWithManifest = graph with
            {
                SchemaVersion = GraphDocument.CurrentSchemaVersion,
                SnapshotId = "test-snapshot",
                Manifest = new AnalysisManifest(
                    [
                        new ManifestInput(
                            "tracelens.config.json",
                            await SnapshotHasher.ComputeFileAsync(configPath),
                            ManifestInputKind.Config)
                    ],
                    ["Services"],
                    AnalysisManifestBuilder.ExcludedDirectories,
                    [],
                    [])
            };
            return new FreshnessFixture(directory, sourcePath, relativePath, graph, graphWithManifest);
        }

        public ValueTask DisposeAsync()
        {
            if (System.IO.Directory.Exists(RootDirectory))
            {
                System.IO.Directory.Delete(RootDirectory, true);
            }

            return ValueTask.CompletedTask;
        }
    }
}

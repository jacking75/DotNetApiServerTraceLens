using System.Security.Cryptography;
using TraceLens.Analyzer.Models;
using TraceLens.Server;
using Xunit;

namespace TraceLens.Server.Tests;

public sealed class GraphFreshnessServiceTests
{
    [Fact]
    public async Task CheckAsync_ReturnsFresh_WhenTrackedFileHasNotChanged()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        var service = new GraphFreshnessService();

        var result = await service.CheckAsync(fixture.Graph);

        Assert.True(result.IsFresh);
        Assert.Empty(result.ChangedFiles);
    }

    [Fact]
    public async Task CheckAsync_ReturnsStale_WhenTrackedFileChangesOrDisappears()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        var service = new GraphFreshnessService();

        await File.WriteAllTextAsync(fixture.SourcePath, "변경된 코드");
        var changed = await service.CheckAsync(fixture.Graph);
        File.Delete(fixture.SourcePath);
        var deleted = await service.CheckAsync(fixture.Graph);

        Assert.False(changed.IsFresh);
        Assert.Equal([fixture.RelativePath], changed.ChangedFiles);
        Assert.False(deleted.IsFresh);
        Assert.Equal([fixture.RelativePath], deleted.ChangedFiles);
    }

    private sealed class FreshnessFixture(
        string directory,
        string sourcePath,
        string relativePath,
        GraphDocument graph) : IAsyncDisposable
    {
        public string SourcePath { get; } = sourcePath;
        public string RelativePath { get; } = relativePath;
        public GraphDocument Graph { get; } = graph;

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
            return new FreshnessFixture(directory, sourcePath, relativePath, graph);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }

            return ValueTask.CompletedTask;
        }
    }
}

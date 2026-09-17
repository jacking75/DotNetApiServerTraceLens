using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// HTTP 밖 실행 단위를 세기만 하는지 본다.
/// 진입점·엣지가 늘어나면 "HTTP 진입점 기준"이라는 그래프의 전제가 깨진다.
/// </summary>
public sealed class EntryPointCandidateTests
{
    [Fact]
    public async Task AnalyzeAsync_CountsNonHttpExecutionUnitsWithoutAddingEntryPoints()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceLensAnalyzer();
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>()
            .AnalyzeAsync(Path.Combine(GetTestsRoot(), "FixturesExternal", "FixtureExternalApp", "FixtureExternalApp.csproj"));

        var candidates = result.Graph.Diagnostics!.EntryPointCandidates;
        Assert.Equal(1, candidates.Single(group => group.Kind == "HostedService").Count);
        Assert.Equal(1, candidates.Single(group => group.Kind == "MessageConsumer").Count);
        Assert.Equal(1, candidates.Single(group => group.Kind == "SignalRHub").Count);
        Assert.Contains("RankBatchWorker", candidates.Single(group => group.Kind == "HostedService").Samples);

        // 후보는 세기만 한다. 진입점도 엣지도 늘지 않는다.
        Assert.Empty(result.Graph.EntryPoints);
        Assert.Contains(
            result.Graph.Diagnostics.EntryPointNotes,
            note => note.Contains("HostedService", StringComparison.Ordinal));
    }

    private static string GetTestsRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetDirectoryName(sourcePath)!;
}

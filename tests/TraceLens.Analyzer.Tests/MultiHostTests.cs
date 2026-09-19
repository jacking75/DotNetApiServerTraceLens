using Microsoft.Extensions.DependencyInjection;
using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// 실행 프로젝트가 둘인 솔루션(T65).
///
/// 단수 필드(<c>startupFlow</c>·<c>middleware</c>)만 보면 두 번째 호스트의 등록과 파이프라인이
/// 통째로 사라진다 — Api + Worker 구조에서 "인증 미들웨어가 없다"는 잘못된 판정이 나온다.
/// </summary>
public sealed class MultiHostTests
{
    [Fact]
    public async Task Analyze_KeepsOneStartupFlowAndPipelinePerExecutableProject()
    {
        var graph = await AnalyzeAsync();

        Assert.Equal(
            ["FixtureApiHost", "FixtureWorkerHost"],
            graph.StartupFlows.Select(flow => flow.ProjectKey).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["FixtureApiHost", "FixtureWorkerHost"],
            graph.MiddlewarePipelines.Select(pipeline => pipeline.ProjectKey).Order(StringComparer.Ordinal));

        // 단수 필드는 호환용으로 목록의 첫 항목과 같아야 한다.
        Assert.Equal(graph.StartupFlows[0].ProgramFile, graph.StartupFlow!.ProgramFile);
        Assert.Equal(graph.MiddlewarePipelines[0].ProgramFile, graph.Middleware!.ProgramFile);

        // 두 번째 호스트에만 있는 미들웨어가 남아 있어야 한다.
        var worker = graph.MiddlewarePipelines.Single(item => item.ProjectKey == "FixtureWorkerHost");
        Assert.Contains(worker.Steps, step => step.Name.Contains("Authentication", StringComparison.Ordinal));
    }

    /// <summary>진입점이 어느 프로젝트 것인지 알 수 있어야 목록을 프로젝트로 묶을 수 있다.</summary>
    [Fact]
    public async Task Analyze_TagsEveryEntryPointWithItsProject()
    {
        var graph = await AnalyzeAsync();

        Assert.Equal("FixtureApiHost", Single(graph, "/api/orders").ProjectKey);
        Assert.Equal("FixtureWorkerHost", Single(graph, "/jobs").ProjectKey);
    }

    private static EntryPoint Single(GraphDocument graph, string route) =>
        Assert.Single(graph.EntryPoints, entry => entry.Route == route);

    private static async Task<GraphDocument> AnalyzeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceLensAnalyzer();
        await using var provider = services.BuildServiceProvider();
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "FixturesMultiHost", "FixtureMultiHost.sln");
        return (await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(path)).Graph;
    }
}

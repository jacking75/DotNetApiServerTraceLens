using Microsoft.AspNetCore.Http;
using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;
using TraceLens.Server.Endpoints;
using Xunit;

namespace TraceLens.Server.Tests;

public sealed class AnalyzeEndpointsTests
{
    [Fact]
    public async Task AnalyzeAsync_ReturnsPreviousAndCurrentAnalyzedAt()
    {
        var previousTime = DateTimeOffset.Parse("2026-08-11T18:00:00+09:00");
        var currentTime = previousTime.AddMinutes(10);
        var previousGraph = CreateGraph(previousTime);
        var currentGraph = CreateGraph(currentTime);
        var graphStore = new GraphStore();
        graphStore.Set(previousGraph);
        var analyzer = new StubAnalyzer(new AnalysisResult(currentGraph, 42, "graph.json"));

        var result = await AnalyzeEndpoints.AnalyzeAsync(
            new AnalyzeRequest(currentGraph.SolutionPath),
            analyzer,
            graphStore,
            CancellationToken.None);

        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var response = Assert.IsType<AnalyzeResponse>(valueResult.Value);
        Assert.Equal(previousTime, response.PreviousAnalyzedAt);
        Assert.Equal(currentTime, response.AnalyzedAt);
        Assert.Same(currentGraph, graphStore.Current);
    }

    private static GraphDocument CreateGraph(DateTimeOffset analyzedAt) => new(
        1,
        "C:\\Fixture\\Fixture.sln",
        analyzedAt,
        [],
        [],
        [],
        new Dictionary<string, string>());

    private sealed class StubAnalyzer(AnalysisResult result) : ITraceLensAnalyzer
    {
        public Task<AnalysisResult> AnalyzeAsync(string solutionPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}

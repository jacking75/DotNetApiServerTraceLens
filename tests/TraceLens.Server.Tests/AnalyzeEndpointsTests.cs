using System.Text;
using System.Text.Json;
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
        var analyzer = new StubAnalyzer(new AnalysisResult(currentGraph, 42, "graph.json", 7, 3, 1024, 5));

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

    [Fact]
    public async Task LoadSavedAsync_ReadsGraphJsonWithoutReanalyzing()
    {
        var solutionDirectory = Path.Combine(Path.GetTempPath(), "tracelens-load-test-" + Guid.NewGuid().ToString("N"));
        var solutionPath = Path.Combine(solutionDirectory, "Fixture.sln");
        var tracelensDirectory = Path.Combine(solutionDirectory, ".tracelens");
        Directory.CreateDirectory(tracelensDirectory);
        try
        {
            var savedAt = DateTimeOffset.Parse("2026-08-11T18:00:00+09:00");
            var savedGraph = CreateGraph(savedAt) with { SolutionPath = solutionPath };
            await File.WriteAllTextAsync(
                Path.Combine(tracelensDirectory, "graph.json"),
                JsonSerializer.Serialize(savedGraph, GraphDocument.JsonOptions));
            var graphStore = new GraphStore();

            var result = await AnalyzeEndpoints.LoadSavedAsync(
                new AnalyzeRequest(solutionPath),
                graphStore,
                CancellationToken.None);

            var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
            var response = Assert.IsType<AnalyzeResponse>(valueResult.Value);
            Assert.Equal(savedAt, response.AnalyzedAt);
            Assert.Null(response.PreviousAnalyzedAt);
            Assert.Equal(savedAt, graphStore.Current?.AnalyzedAt);
        }
        finally
        {
            Directory.Delete(solutionDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadSavedAsync_ReturnsNotFound_WhenNoSavedGraphExists()
    {
        var solutionDirectory = Path.Combine(Path.GetTempPath(), "tracelens-load-missing-" + Guid.NewGuid().ToString("N"));
        var solutionPath = Path.Combine(solutionDirectory, "Fixture.sln");
        var graphStore = new GraphStore();

        var result = await AnalyzeEndpoints.LoadSavedAsync(
            new AnalyzeRequest(solutionPath),
            graphStore,
            CancellationToken.None);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusResult.StatusCode);
        Assert.Null(graphStore.Current);
    }

    [Fact]
    public async Task LoadSavedAsync_FindsGraphInAncestorDirectory_ForProjectTarget()
    {
        // 분석기는 참조 프로젝트까지 담는 공통 상위 폴더에 결과를 쓴다. 복원은 대상 폴더만 보면 안 된다.
        var rootDirectory = Path.Combine(Path.GetTempPath(), "tracelens-load-ancestor-" + Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(rootDirectory, "GameServer");
        var projectPath = Path.Combine(projectDirectory, "GameServer.csproj");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(Path.Combine(rootDirectory, ".tracelens"));
        try
        {
            var savedAt = DateTimeOffset.Parse("2026-09-17T18:00:00+09:00");
            var savedGraph = CreateGraph(savedAt) with { SolutionPath = projectPath, RootDirectory = rootDirectory };
            await File.WriteAllTextAsync(
                Path.Combine(rootDirectory, ".tracelens", "graph.json"),
                JsonSerializer.Serialize(savedGraph, GraphDocument.JsonOptions));
            var graphStore = new GraphStore();

            var result = await AnalyzeEndpoints.LoadSavedAsync(
                new AnalyzeRequest(projectPath),
                graphStore,
                CancellationToken.None);

            var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
            var response = Assert.IsType<AnalyzeResponse>(valueResult.Value);
            Assert.Equal(savedAt, response.AnalyzedAt);
            Assert.Equal(projectPath, graphStore.Current?.SolutionPath);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadSavedAsync_IgnoresAncestorGraphOfAnotherTarget()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "tracelens-load-other-" + Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(rootDirectory, "GameServer");
        var projectPath = Path.Combine(projectDirectory, "GameServer.csproj");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(Path.Combine(rootDirectory, ".tracelens"));
        try
        {
            var savedGraph = CreateGraph(DateTimeOffset.UtcNow) with
            {
                SolutionPath = Path.Combine(rootDirectory, "AccountServer", "AccountServer.csproj")
            };
            await File.WriteAllTextAsync(
                Path.Combine(rootDirectory, ".tracelens", "graph.json"),
                JsonSerializer.Serialize(savedGraph, GraphDocument.JsonOptions));
            var graphStore = new GraphStore();

            var result = await AnalyzeEndpoints.LoadSavedAsync(
                new AnalyzeRequest(projectPath),
                graphStore,
                CancellationToken.None);

            var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
            Assert.Equal(StatusCodes.Status404NotFound, statusResult.StatusCode);
            Assert.Null(graphStore.Current);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_ReadsPostedGraphJson()
    {
        var analyzedAt = DateTimeOffset.Parse("2026-08-11T18:00:00+09:00");
        var graph = CreateGraph(analyzedAt);
        var json = JsonSerializer.Serialize(graph, GraphDocument.JsonOptions);
        var context = new DefaultHttpContext
        {
            Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes(json)) },
        };
        var graphStore = new GraphStore();

        var result = await AnalyzeEndpoints.ImportAsync(context, graphStore, CancellationToken.None);

        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var response = Assert.IsType<AnalyzeResponse>(valueResult.Value);
        Assert.Equal(analyzedAt, response.AnalyzedAt);
        Assert.Null(response.PreviousAnalyzedAt);
        Assert.Equal(graph.SolutionPath, graphStore.Current?.SolutionPath);
    }

    [Fact]
    public async Task ImportAsync_ReturnsBadRequest_ForInvalidJson()
    {
        var context = new DefaultHttpContext
        {
            Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes("이건 JSON이 아니다")) },
        };
        var graphStore = new GraphStore();

        var result = await AnalyzeEndpoints.ImportAsync(context, graphStore, CancellationToken.None);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusResult.StatusCode);
        Assert.Null(graphStore.Current);
    }

    private static GraphDocument CreateGraph(DateTimeOffset analyzedAt) => new(
        1,
        "C:\\Fixture\\Fixture.sln",
        analyzedAt,
        [],
        [],
        [],
        [],
        new Dictionary<string, string>());

    private sealed class StubAnalyzer(AnalysisResult result) : ITraceLensAnalyzer
    {
        public Task<AnalysisResult> AnalyzeAsync(
            string solutionPath,
            IProgress<AnalysisProgress>? progress = null,
            CancellationToken cancellationToken = default,
            string? outputDirectory = null,
            string? configPath = null) =>
            Task.FromResult(result);
    }
}

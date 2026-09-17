using Microsoft.Extensions.DependencyInjection;
using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// T17에서 넓힌 분석 범위의 회귀 테스트다.
/// virtual/override 디스패치, <c>[AcceptVerbs]</c>·<c>[NonAction]</c>·버전 토큰, 다중 호스트를 확인한다.
/// </summary>
public sealed class AnalyzerScopeTests
{
    /// <summary>재정의가 하나뿐이면 기반 선언 엣지와 함께 재정의로 가는 추정 엣지도 만든다.</summary>
    [Fact]
    public async Task Resolve_AddsEdgeToTheOnlyOverride()
    {
        var graph = await AnalyzeAsync();
        const string probe = "FixtureApp.Services.DispatchProbe.Run(int)";

        Assert.Contains(graph.Edges, edge =>
            edge.From == probe && edge.To == "FixtureApp.Services.ReportFormatterBase.Format(int)");
        var overrideEdge = Assert.Single(graph.Edges, edge =>
            edge.From == probe && edge.To == "FixtureApp.Services.JsonReportFormatter.Format(int)");
        Assert.Equal(CallConfidence.Estimated, overrideEdge.Confidence);
    }

    /// <summary>재정의가 여럿이면 엣지를 만들지 않고 경계로 남긴다.</summary>
    [Fact]
    public async Task Resolve_LeavesMultipleOverridesUnresolved()
    {
        var graph = await AnalyzeAsync();

        Assert.DoesNotContain(graph.Edges, edge => edge.To == "FixtureApp.Services.CsvExport.Export(int)");
        Assert.DoesNotContain(graph.Edges, edge => edge.To == "FixtureApp.Services.XmlExport.Export(int)");
        Assert.Contains(graph.CallSites, site =>
            site.To.Length == 0 && site.Note.Contains("재정의가 2곳", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EntryPoints_ReadAcceptVerbsAndSkipNonAction()
    {
        var graph = await AnalyzeAsync();

        var items = graph.EntryPoints
            .Where(item => item.Route.Contains("/items", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, items.Length);
        Assert.Contains(items, item => item.HttpMethod == "GET");
        Assert.Contains(items, item => item.HttpMethod == "HEAD");

        // [NonAction]이 붙은 메서드는 라우팅 대상이 아니다.
        Assert.DoesNotContain(graph.EntryPoints, item => item.Route.Contains("hidden", StringComparison.Ordinal));
    }

    /// <summary>버전 토큰의 제약만 지우고 리터럴은 그대로 둔다. 원문 라우트는 쓴 대로 남는다.</summary>
    [Fact]
    public async Task EntryPoints_NormalizeVersionTokenWithoutMergingRoutes()
    {
        var graph = await AnalyzeAsync();

        var item = graph.EntryPoints.First(entry => entry.Route.Contains("/items", StringComparison.Ordinal));
        Assert.Contains("v{version:apiVersion}", item.Route, StringComparison.Ordinal);
        Assert.Equal("/api/v{version}/versioned/items", item.NormalizedRoute);
    }

    /// <summary>실행 가능한 프로젝트가 하나면 목록에도 하나만 들어가고 단수 필드와 같다.</summary>
    [Fact]
    public async Task StartupFlows_MirrorTheSingularFieldForOneHost()
    {
        var graph = await AnalyzeAsync();

        Assert.NotNull(graph.StartupFlow);
        Assert.Single(graph.StartupFlows);
        Assert.Equal(graph.StartupFlow!.ProgramFile, graph.StartupFlows[0].ProgramFile);
        Assert.Equal("FixtureApp", graph.StartupFlows[0].ProjectKey);

        Assert.NotNull(graph.Middleware);
        Assert.Single(graph.MiddlewarePipelines);
        Assert.Equal(graph.Middleware!.ProgramFile, graph.MiddlewarePipelines[0].ProgramFile);
    }

    private static async Task<GraphDocument> AnalyzeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceLensAnalyzer();
        await using var provider = services.BuildServiceProvider();
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "FixtureApp", "FixtureApp.csproj");
        return (await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(path)).Graph;
    }
}

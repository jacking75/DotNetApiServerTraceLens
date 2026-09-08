using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// Minimal API 진입점(§7.1)의 회귀 대상이다.
/// 고정 라우트·중첩 그룹·메서드 그룹·람다·로컬 함수·MapMethods·동적 라우트를 한 픽스처에서 확인한다.
/// </summary>
public sealed class MinimalApiEntryPointTests(MinimalApiAppFixture fixture) : IClassFixture<MinimalApiAppFixture>
{
    [Fact]
    public async Task Scan_FindsGroupPrefixedRoutes()
    {
        var graph = (await fixture.Result).Graph;

        Assert.Contains(graph.EntryPoints, item => item is { HttpMethod: "GET", Route: "/api/ping" });
        Assert.Contains(graph.EntryPoints, item => item is { HttpMethod: "GET", Route: "/api/alias" });
        Assert.All(
            graph.EntryPoints.Where(item => item.Source == EndpointSource.MinimalApi),
            item => Assert.StartsWith("/", item.Route, StringComparison.Ordinal));
    }

    /// <summary>같은 핸들러를 두 라우트에 걸어도 엔드포인트가 두 개여야 한다(노드 ID로 접히면 안 된다).</summary>
    [Fact]
    public async Task Scan_KeepsBothRoutesOfTheSameHandler()
    {
        var graph = (await fixture.Result).Graph;

        var aliases = graph.EntryPoints.Where(item => item.Route.StartsWith("/api/alias", StringComparison.Ordinal)).ToArray();

        Assert.Equal(2, aliases.Length);
        Assert.Single(aliases.Select(item => item.NodeId).Distinct(StringComparer.Ordinal));
        Assert.Equal(2, aliases.Select(item => item.EndpointId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>중첩 그룹과 확장 메서드 안의 등록도 접두사를 채워야 한다.</summary>
    [Fact]
    public async Task Scan_ResolvesNestedGroupsInsideRegistrarExtensionMethod()
    {
        var graph = (await fixture.Result).Graph;

        Assert.Contains(graph.EntryPoints, item => item.Route == "/api/v2/catalog/items/{sku}");
        Assert.Contains(graph.EntryPoints, item => item.Route == "/api/v2/catalog/items/{sku}/audit");
    }

    /// <summary>MapMethods는 동사마다 엔드포인트를 만든다.</summary>
    [Fact]
    public async Task Scan_ExpandsMapMethodsIntoOneEndpointPerVerb()
    {
        var graph = (await fixture.Result).Graph;

        var touch = graph.EntryPoints
            .Where(item => item.Route == "/api/v2/catalog/items/{sku}/touch")
            .Select(item => item.HttpMethod)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["PATCH", "PUT"], touch);
    }

    /// <summary>메서드 그룹·람다·로컬 함수가 각각 다른 실행 단위로 잡혀야 한다.</summary>
    [Fact]
    public async Task Scan_ClassifiesHandlerKinds()
    {
        var graph = (await fixture.Result).Graph;
        var minimal = graph.EntryPoints.Where(item => item.Source == EndpointSource.MinimalApi).ToArray();

        Assert.Contains(minimal, item => item.HandlerKind == HandlerKind.Lambda);
        Assert.Contains(minimal, item => item.HandlerKind == HandlerKind.MethodGroup);
        Assert.Contains(minimal, item => item.HandlerKind == HandlerKind.LocalFunction);
        Assert.All(minimal, item => Assert.Contains(graph.Nodes, node => node.Id == item.NodeId));
    }

    /// <summary>이름만 <c>MapGet</c>인 사용자 메서드는 진입점이 아니다.</summary>
    [Fact]
    public async Task Scan_IgnoresUserMethodsNamedLikeMapGet()
    {
        var graph = (await fixture.Result).Graph;

        Assert.DoesNotContain(graph.EntryPoints, item => item.Route.Contains("not-an-endpoint", StringComparison.Ordinal));
    }

    /// <summary>라우트가 상수가 아니면 확정하지 않고 추정으로 남긴다.</summary>
    [Fact]
    public async Task Scan_MarksDynamicRouteAsEstimated()
    {
        var graph = (await fixture.Result).Graph;

        var dynamicRoute = graph.EntryPoints
            .FirstOrDefault(item => item.Route.Contains("dynamicroute", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(dynamicRoute);
        Assert.Equal(CallConfidence.Estimated, dynamicRoute.Confidence);
        Assert.Contains(dynamicRoute.Notes, note => note.Contains("상수", StringComparison.Ordinal));
    }

    /// <summary>람다 핸들러도 호출 그래프의 시작점이 되어야 한다(analysisScope=solution 기본값에서도).</summary>
    [Fact]
    public async Task Scan_LambdaHandlerBecomesGraphNodeWithCallEdges()
    {
        var graph = (await fixture.Result).Graph;

        var ping = graph.EntryPoints.Single(item => item.Route == "/api/ping");
        var handler = Assert.Single(graph.Nodes, node => node.Id == ping.NodeId);

        Assert.Equal(CallableKind.Lambda, handler.Callable);
        Assert.Contains(graph.Edges, edge => edge.From == handler.Id && edge.To.Contains("CatalogService.Describe", StringComparison.Ordinal));
    }

    /// <summary>호출 근거에는 실제 호출 줄과 해석 종류가 들어 있어야 한다.</summary>
    [Fact]
    public async Task Scan_RecordsCallSiteEvidence()
    {
        var graph = (await fixture.Result).Graph;

        Assert.NotEmpty(graph.CallSites);
        Assert.Contains(graph.CallSites, site =>
            site.Resolution == CallResolution.DiRegistration && site.Line > 0 && site.File.Length > 0);
        Assert.All(graph.CallSites, site => Assert.True(site.Line > 0));
    }

    /// <summary>호출되지 않은 중첩 본문의 호출을 부모의 직접 호출로 표시하면 안 된다.</summary>
    [Fact]
    public async Task Scan_DoesNotAttributeLambdaBodyCallsToEnclosingMethod()
    {
        var graph = (await fixture.Result).Graph;

        var topLevel = graph.Nodes.FirstOrDefault(node => node.Id.Contains("<Main>$", StringComparison.Ordinal) &&
                                                          node.Callable == CallableKind.Method);
        if (topLevel is null)
        {
            return;
        }

        Assert.DoesNotContain(
            graph.Edges,
            edge => edge.From == topLevel.Id && edge.To.Contains("CatalogService.Describe", StringComparison.Ordinal));
    }
}

/// <summary>MSBuild 로드가 느려서 픽스처를 한 번만 분석한다.</summary>
public sealed class MinimalApiAppFixture
{
    private readonly Lazy<Task<AnalysisResult>> analysis = new(() =>
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTraceLensAnalyzer();
        return services.BuildServiceProvider().GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(ProjectPath);
    });

    public Task<AnalysisResult> Result => analysis.Value;

    public static string ProjectPath => Path.Combine(
        GetRepositoryRoot(),
        "tests",
        "TraceLens.Analyzer.Tests",
        "FixturesMinimalApi",
        "MinimalApiApp",
        "MinimalApiApp.csproj");

    private static string GetRepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}

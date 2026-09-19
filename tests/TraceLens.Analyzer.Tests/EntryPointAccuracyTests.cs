using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// 존재하지 않는 라우트를 "확실"로 보여 주거나 버전이 다른 두 API를 하나로 뭉개지 않는지 본다.
/// 둘 다 화면의 숫자를 그대로 믿는 사람에게 직접 피해를 주는 오류다.
/// </summary>
[Collection(FixtureClassicAppCollection.Name)]
public sealed class EntryPointAccuracyTests
{
    [Fact]
    public async Task AnalyzeAsync_MakesOneRoutePerDerivedControllerForBaseClassActions()
    {
        var graph = await AnalyzeAsync();

        var routes = graph.EntryPoints.Select(item => item.Route).ToArray();
        Assert.Contains("/api/Products/{id:int}", routes);
        Assert.Contains("/api/Customers/{id:int}", routes);

        // 기반 클래스 이름이 든 라우트는 나오면 안 된다.
        Assert.DoesNotContain(routes, route => route.Contains("CrudControllerBase", StringComparison.Ordinal));

        // 파생별로 endpointId가 갈라져야 목록에서 구분된다.
        var derived = graph.EntryPoints.Where(item => item.Route.EndsWith("/{id:int}", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, derived.Length);
        Assert.Equal(2, derived.Select(item => item.EndpointId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(derived, item => Assert.Contains(item.Notes, note => note.Contains("상속", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AnalyzeAsync_SplitsEntryPointsByApiVersion()
    {
        var graph = await AnalyzeAsync();

        var routes = graph.EntryPoints.Select(item => item.Route).ToArray();
        Assert.Contains("/api/v1.0/catalog/items", routes);
        Assert.Contains("/api/v2.0/catalog/items", routes);

        // 토큰이 없는 버전은 라우트가 같고 endpointId로만 구분된다.
        var legacy = graph.EntryPoints.Single(item => item.Route == "/api/legacy/items");
        Assert.Equal(["3.0"], legacy.ApiVersions);
    }

    /// <summary>
    /// README가 "[Route] 지원"이라고 적었는데 메서드에 붙은 [Route]는 읽지 않아
    /// 존재하지 않는 라우트가 "확실"로 나왔다(T54).
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ReadsMethodRouteAttribute()
    {
        var graph = await AnalyzeAsync();

        var routes = graph.EntryPoints.Select(item => item.Route).ToArray();

        // 진입 특성에 템플릿이 없고 메서드 [Route]만 있는 경우.
        Assert.Contains("/api/orders/items", routes);
        // 클래스 라우트만 쓴 잘못된 라우트가 남아 있으면 안 된다.
        Assert.DoesNotContain("/api/orders", routes);

        // 둘 다 있으면 서로를 대체하지 않고 각각 별도 라우트가 된다.
        Assert.Contains("/api/orders/summary", routes);
        Assert.Contains("/api/orders/legacy-summary", routes);
    }

    [Fact]
    public async Task AnalyzeAsync_FindsHeadAndOptionsEntryPoints()
    {
        var graph = await AnalyzeAsync();

        var ping = graph.EntryPoints.Where(item => item.Route == "/api/orders/ping").ToArray();

        Assert.Contains(ping, item => item.HttpMethod == "HEAD");
        Assert.Contains(ping, item => item.HttpMethod == "OPTIONS");
    }

    /// <summary>비추상 기반 컨트롤러는 기반 라우트와 파생 라우트가 모두 실재한다.</summary>
    [Fact]
    public async Task AnalyzeAsync_MakesRoutesForNonAbstractBaseController()
    {
        var graph = await AnalyzeAsync();

        var routes = graph.EntryPoints.Select(item => item.Route).ToArray();

        Assert.Contains("/api/SearchControllerBase/query", routes);
        Assert.Contains("/api/ProductSearch/query", routes);
    }

    /// <summary>숨긴 API는 지우지 않고 구분만 한다 — 라우트는 실제로 존재한다.</summary>
    [Fact]
    public async Task AnalyzeAsync_MarksApiExplorerIgnoredEntryPoints()
    {
        var graph = await AnalyzeAsync();

        var hidden = Assert.Single(graph.EntryPoints, item => item.Route == "/api/orders/internal");

        Assert.True(hidden.HiddenFromApiExplorer);
        Assert.DoesNotContain(graph.EntryPoints.Where(item => item.Route != "/api/orders/internal"),
            item => item.HiddenFromApiExplorer);
    }

    /// <summary>
    /// 재정의 안의 `base.Foo()`가 자기 자신으로 가는 순환 엣지가 되면 재귀처럼 보여
    /// 사람도 에이전트도 오독한다(T53).
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_DoesNotMakeSelfLoop_ForBaseQualifiedCall()
    {
        var graph = await AnalyzeAsync();

        const string derived = "FixtureClassicApp.Services.OrderHandler.HandleAsync(int)";
        const string baseDeclaration = "FixtureClassicApp.Services.BaseHandler.HandleAsync(int)";

        // base.HandleAsync()는 런타임 타입과 무관하게 기반 선언이 실행된다. 자기 순환 엣지를 만들면 안 된다.
        Assert.DoesNotContain(graph.Edges, edge => edge.From == derived && edge.To == derived);
        var edge = Assert.Single(graph.Edges, item => item.From == derived && item.To == baseDeclaration);
        Assert.Equal(Models.CallConfidence.Certain, edge.Confidence);

        // 그래프 어디에도 자기 순환 엣지가 없어야 한다.
        Assert.DoesNotContain(graph.Edges, item => item.From == item.To);

        // base. 한정자가 없는 가상 호출은 지금처럼 재정의 추정 엣지를 그대로 둔다.
        Assert.Contains(graph.Edges, item =>
            item.From == "FixtureClassicApp.Services.OrderHandler.RunAsync(int)" &&
            item.To == "FixtureClassicApp.Services.BaseHandler.ValidateAsync(int)");
    }

    private static async Task<Models.GraphDocument> AnalyzeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceLensAnalyzer();
        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(Path.Combine(
            GetTestsRoot(), "FixturesClassic", "FixtureClassicApp", "FixtureClassicApp.csproj"));
        return result.Graph;
    }

    private static string GetTestsRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetDirectoryName(sourcePath)!;
}

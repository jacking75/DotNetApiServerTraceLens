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

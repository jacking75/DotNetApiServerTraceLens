using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// T13에서 고친 정확도 버그의 회귀 테스트다.
/// 제네릭·확장 메서드의 노드 ID, 절대 메서드 라우트, 기반 클래스 <c>[Route]</c>, 설정 파일 검증을 확인한다.
/// </summary>
public sealed class AnalyzerAccuracyTests
{
    [Fact]
    public async Task AnalyzeAsync_KeepsEdgesToGenericAndExtensionMethods()
    {
        await using var provider = CreateProvider();

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(FixturePath);

        // 호출 지점은 Repository<OrderRow>.GetAsync이고 선언은 Repository<T>.GetAsync다.
        // 노드 ID를 선언 기준으로 정규화하지 않으면 대상 노드를 찾지 못해 엣지가 버려진다.
        Assert.Contains(result.Graph.Nodes, node => node.Id == "FixtureApp.Repositories.Repository<T>.GetAsync(int)");
        Assert.Contains(result.Graph.Edges, edge =>
            edge.From == "FixtureApp.Controllers.HealthController.GetOrder(int)" &&
            edge.To == "FixtureApp.Repositories.Repository<T>.GetAsync(int)");

        // 확장 메서드는 축약형(this 수신자)이라 호출 지점의 파라미터 수가 다르다. 축약 전 정적 메서드를 기준으로 삼는다.
        Assert.Contains(result.Graph.Edges, edge =>
            edge.From == "FixtureApp.Controllers.HealthController.GetOrder(int)" &&
            edge.To.Contains("RepositoryExtensions.GetOrDefaultAsync", StringComparison.Ordinal));
        Assert.Contains(result.Graph.Edges, edge =>
            edge.From.Contains("RepositoryExtensions.GetOrDefaultAsync", StringComparison.Ordinal) &&
            edge.To == "FixtureApp.Repositories.Repository<T>.GetAsync(int)");
    }

    [Fact]
    public async Task AnalyzeAsync_TreatsLeadingSlashMethodRouteAsAbsolute()
    {
        await using var provider = CreateProvider();

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(FixturePath);

        // [Route("api/v1/[controller]")]가 기반 클래스에 있어도 [HttpGet("/health")]는 절대 경로다.
        var health = Assert.Single(result.Graph.EntryPoints, item => item.Route == "/health");
        Assert.Equal("GET", health.HttpMethod);

        // 상대 경로는 기반 클래스의 접두사를 이어받는다.
        Assert.Contains(result.Graph.EntryPoints, item => item.Route == "/api/v1/Health/orders/{id:int}");
    }

    [Theory]
    [InlineData("{ \"maxCallDepth\": \"셋\" }", "maxCallDepth")]
    [InlineData("{ \"includeMembers\": \"yes\" }", "includeMembers")]
    [InlineData("{ \"모르는키\": 1 }", "모르는키")]
    public void Config_ReportsIssuesInsteadOfThrowing(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);

        var result = TraceLensConfig.Read(document.RootElement);

        Assert.Contains(result.Issues, issue => issue.Message.Contains(expected, StringComparison.Ordinal));
        // 문제가 있어도 분석은 기본 설정으로 진행한다.
        Assert.Equal(TraceLensConfig.Default.MaxCallDepth, result.Config.MaxCallDepth);
        Assert.Equal(TraceLensConfig.Default.IncludeMembers, result.Config.IncludeMembers);
    }

    [Fact]
    public async Task Config_FallsBackToDefaults_WhenJsonIsBroken()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "tracelens.config.json"), "{ 이건 JSON이 아니다");

            var result = await TraceLensConfig.LoadAsync(directory);

            Assert.Single(result.Issues, issue => issue.Severity == ConfigIssueSeverity.Error);
            Assert.Equal(TraceLensConfig.Default.MaxCallDepth, result.Config.MaxCallDepth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Config_ReadsValidValues()
    {
        using var document = JsonDocument.Parse("""{ "maxCallDepth": 9, "includeMembers": false }""");

        var result = TraceLensConfig.Read(document.RootElement);

        Assert.Empty(result.Issues);
        Assert.Equal(9, result.Config.MaxCallDepth);
        Assert.False(result.Config.IncludeMembers);
    }

    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "FixtureApp", "FixtureApp.csproj");

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceLensAnalyzer();
        return services.BuildServiceProvider();
    }
}

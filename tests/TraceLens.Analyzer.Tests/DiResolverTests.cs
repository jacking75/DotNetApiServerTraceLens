using Microsoft.Extensions.DependencyInjection;
using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// DI 등록 모양별 판정. 확정할 수 있는 것만 확정하고, 나머지는 사유를 남긴다.
/// </summary>
[Collection(FixtureAppCollection.Name)]
public sealed class DiResolverTests
{
    [Theory]
    // 자기 등록 + GetRequiredService 팩토리: 구현체를 확정할 수 있다.
    [InlineData("FixtureApp.Services.SelfRegistered.PingAsync()")]
    // 단일 new 팩토리: 확정할 수 있다.
    [InlineData("FixtureApp.Services.FactoryRegistered.PingAsync()")]
    // open generic: typeof(IStore<>) → typeof(MemoryStore<>).
    [InlineData("FixtureApp.Services.MemoryStore<T>.LoadAsync(int)")]
    public async Task Resolve_ConnectsRegisteredImplementations(string expectedTarget)
    {
        var graph = await AnalyzeAsync();

        Assert.Contains(graph.Edges, edge =>
            edge.From == "FixtureApp.Services.RegistrationProbe.RunAsync()" && edge.To == expectedTarget);
    }

    /// <summary>조건부 등록은 잇되 "실행 시 다른 구현체가 쓰일 수 있다"를 근거에 남긴다.</summary>
    [Fact]
    public async Task Resolve_MarksConditionalRegistrationAsEstimated()
    {
        var graph = await AnalyzeAsync();

        var site = Assert.Single(graph.CallSites, item =>
            item.DeclaredTarget.Contains("IConditional.PingAsync", StringComparison.Ordinal));
        Assert.Equal(CallConfidence.Estimated, site.Confidence);
        Assert.Contains("조건부", site.Note);
    }

    /// <summary>갈래마다 다른 구현체를 등록하면 확정하지 않고 경계로 남긴다.</summary>
    [Fact]
    public async Task Resolve_LeavesBranchedRegistrationsUnresolved()
    {
        var graph = await AnalyzeAsync();

        var site = Assert.Single(graph.CallSites, item =>
            item.DeclaredTarget.Contains("IEnvironmentSpecific.PingAsync", StringComparison.Ordinal));
        Assert.Equal(string.Empty, site.To);
        Assert.Contains("2종", site.Note);
    }

    /// <summary>팩토리 본문을 해석하지 못하고 구현체도 여럿이면 엣지를 만들지 않고 경계로 남긴다.</summary>
    [Fact]
    public async Task Resolve_LeavesUnresolvableFactoryAsBoundary()
    {
        var graph = await AnalyzeAsync();

        var site = Assert.Single(graph.CallSites, item =>
            item.DeclaredTarget.Contains("IAmbiguousFactory.PingAsync", StringComparison.Ordinal));
        Assert.Equal(string.Empty, site.To);
        Assert.Equal(CallResolution.Ambiguous, site.Resolution);
        Assert.DoesNotContain(graph.Edges, edge =>
            edge.To.Contains("FirstAmbiguous.PingAsync", StringComparison.Ordinal));
    }

    /// <summary>
    /// 테스트 더블이 구현체 후보로 세어지면 "구현체 2개"가 되어 유일 구현체 추정이 꺼진다.
    /// `excludeProjectNamePatterns` 기본값이 빈 배열이라 대부분의 사용자가 이 상태로 쓴다(T59).
    /// </summary>
    [Fact]
    public async Task Resolve_IgnoresTestDoublesWhenPickingSingleImplementation()
    {
        var graph = await AnalyzeSolutionAsync();

        // 테스트 프로젝트에 FakeAuditService가 있어도 주 프로젝트의 AuditService로 이어져야 한다.
        Assert.Contains(graph.Edges, edge =>
            edge.From == "FixtureApp.Controllers.OrdersController.Create(int)" &&
            edge.To == "FixtureApp.Services.AuditService.RecordAsync(int)");

        // 뺐다는 사실이 근거에 남아야 한다. 조용히 빼면 "왜 이 구현체인가"를 알 수 없다.
        var site = Assert.Single(graph.CallSites, item =>
            item.From == "FixtureApp.Controllers.OrdersController.Create(int)" &&
            item.DeclaredTarget.Contains("IAuditService.RecordAsync", StringComparison.Ordinal));
        Assert.Contains("FakeAuditService", site.Note);

        // 테스트 프로젝트는 진단에 표시된다(제외가 아니라 표시다).
        Assert.Contains(graph.Diagnostics!.Projects, project => project.IsTest && project.Name == "FixtureApp.Tests");
    }

    private static async Task<GraphDocument> AnalyzeSolutionAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceLensAnalyzer();
        await using var provider = services.BuildServiceProvider();
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "FixtureAppWithTests.sln");
        return (await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(path)).Graph;
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

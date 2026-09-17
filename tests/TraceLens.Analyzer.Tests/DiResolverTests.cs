using Microsoft.Extensions.DependencyInjection;
using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// DI 등록 모양별 판정. 확정할 수 있는 것만 확정하고, 나머지는 사유를 남긴다.
/// </summary>
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

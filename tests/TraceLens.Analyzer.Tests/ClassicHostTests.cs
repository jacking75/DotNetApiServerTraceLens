using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// .NET Core 3.1~5 구조(<c>Program.cs</c> + <c>Startup.cs</c>)에서 시작 흐름·미들웨어가 채워지는지 본다.
/// 예전에는 <c>Program.cs</c>를 먼저 찾고 거기서 멈춰 두 보기가 통째로 비었고, 이유도 결과에 남지 않았다.
/// </summary>
public sealed class ClassicHostTests
{
    [Fact]
    public async Task AnalyzeAsync_ReadsRegistrationsAndPipelineFromStartup()
    {
        await using var provider = CreateProvider();

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetClassicProjectPath());

        var flow = result.Graph.StartupFlow;
        Assert.NotNull(flow);
        Assert.Contains(flow.ServiceSteps, step => step.ServiceType == "IReportService");
        Assert.Contains(flow.ServiceSteps, step => step.ServiceType == "IClock");

        var middleware = result.Graph.Middleware;
        Assert.NotNull(middleware);
        Assert.Contains(middleware.Steps, step => step.Name.Contains("Authentication", StringComparison.Ordinal));
        Assert.Contains(middleware.Steps, step => step.Name.Contains("Authorization", StringComparison.Ordinal));

        // 파이프라인 순서는 Startup.Configure에 쓴 순서 그대로여야 한다.
        var authentication = middleware.Steps.First(step => step.Name.Contains("Authentication", StringComparison.Ordinal));
        var authorization = middleware.Steps.First(step => step.Name.Contains("Authorization", StringComparison.Ordinal));
        Assert.True(authentication.Order < authorization.Order);
    }

    /// <summary>최소 호스팅 구조의 결과가 바뀌지 않아야 한다. 클래식 지원이 기존 경로를 건드리면 안 된다.</summary>
    [Fact]
    public async Task AnalyzeAsync_KeepsMinimalHostingResultUnchanged()
    {
        await using var provider = CreateProvider();

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetDataFixtureSolutionPath());

        Assert.NotNull(result.Graph.StartupFlow);
        Assert.NotEmpty(result.Graph.StartupFlow.ServiceSteps);
        Assert.NotNull(result.Graph.Middleware);
        Assert.NotEmpty(result.Graph.Middleware.Steps);
    }

    [Fact]
    public async Task AnalyzeAsync_SaysWhyTheHostWasNotFound()
    {
        await using var provider = CreateProvider();

        // 호스트 구성이 없는 프로젝트(라이브러리 성격)다. 두 보기가 비는 이유가 한계에 남아야 한다.
        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetExternalProjectPath());

        Assert.Null(result.Graph.StartupFlow);
        Assert.Contains(
            result.Graph.Diagnostics!.Limitations,
            line => line.Contains("호스트 구성을 찾지 못했다", StringComparison.Ordinal));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceLensAnalyzer();
        return services.BuildServiceProvider();
    }

    private static string GetClassicProjectPath() => Path.Combine(
        GetTestsRoot(), "FixturesClassic", "FixtureClassicApp", "FixtureClassicApp.csproj");

    private static string GetExternalProjectPath() => Path.Combine(
        GetTestsRoot(), "FixturesExternal", "FixtureExternalApp", "FixtureExternalApp.csproj");

    private static string GetDataFixtureSolutionPath() => Path.Combine(
        GetTestsRoot(), "FixturesData", "FixtureDataApp.sln");

    private static string GetTestsRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetDirectoryName(sourcePath)!;
}

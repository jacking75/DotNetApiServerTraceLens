using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// <c>FixtureDataApp</c>을 한 번만 분석해 여러 테스트 클래스가 함께 본다.
/// MSBuild 로드가 느린 데다, 같은 솔루션을 동시에 분석하면 <c>.tracelens/graph.json</c> 쓰기가 겹친다.
/// </summary>
public sealed class FixtureDataAppFixture
{
    private readonly Lazy<Task<AnalysisResult>> analysis = new(() =>
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTraceLensAnalyzer();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(SolutionPath);
    });

    public Task<AnalysisResult> Result => analysis.Value;

    public static string SolutionPath =>
        Path.Combine(GetRepositoryRoot(), "tests", "TraceLens.Analyzer.Tests", "FixturesData", "FixtureDataApp.sln");

    private static string GetRepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}

[CollectionDefinition(Name)]
public sealed class FixtureDataAppCollection : ICollectionFixture<FixtureDataAppFixture>
{
    public const string Name = "FixtureDataApp";
}

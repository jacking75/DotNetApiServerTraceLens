using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// <c>samples/samples.json</c>의 <c>expected</c>는 사용자가 결과를 대조하는 정답지다.
/// 여기가 틀리면 도구를 처음 쓰는 사람이 "내가 잘못했나"로 읽고 신뢰를 잃는다.
/// 그래서 예제를 실제로 분석해 숫자와 라우트를 대조한다.
/// </summary>
public sealed class SampleAnswerKeyTests
{
    [Fact]
    public async Task Samples_AnswerKeyMatchesTheRealAnalysis()
    {
        var samples = ReadSamples();
        var orderApi = samples.Single(sample => sample.GetProperty("id").GetString() == "order-api");

        await using var provider = CreateProvider();
        var result = await provider.GetRequiredService<ITraceLensAnalyzer>()
            .AnalyzeAsync(Path.Combine(GetRepositoryRoot(), "samples", "OrderApi", "OrderApi.csproj"));

        var controllers = result.Graph.EntryPoints.Count(item => item.Source == EndpointSource.Controller);
        var minimalApis = result.Graph.EntryPoints.Count(item => item.Source == EndpointSource.MinimalApi);

        Assert.Equal(4, controllers);
        Assert.Equal(4, minimalApis);
        Assert.Equal(8, result.Graph.EntryPoints.Count);

        var expected = string.Join("\n", orderApi.GetProperty("expected").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains($"진입점 {result.Graph.EntryPoints.Count}개", expected, StringComparison.Ordinal);
        Assert.Contains($"컨트롤러 {controllers}개", expected, StringComparison.Ordinal);
        Assert.Contains($"Minimal API {minimalApis}", expected, StringComparison.Ordinal);

        // 정답지가 나열한 Minimal API 라우트가 실제 결과에 모두 있어야 한다.
        var routes = result.Graph.EntryPoints.Select(item => item.Route).ToHashSet(StringComparer.Ordinal);
        foreach (var route in new[]
        {
            "/api/v1/health",
            "/api/v1/orders",
            "/api/v1/reports/{customerId}",
            "/api/v1/reports/{customerId}/short",
        })
        {
            Assert.Contains(route, expected, StringComparison.Ordinal);
            Assert.Contains(route, routes);
        }
    }

    /// <summary>같은 소스 폴더를 가리키는 두 학습 경로는 프로젝트 하나다. 설명이 "프로젝트 2개"로 읽히면 안 된다.</summary>
    [Fact]
    public void Samples_ShareOneSourceDirectory()
    {
        var directories = ReadSamples()
            .Select(sample => sample.GetProperty("sourceDirectory").GetString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Single(directories);
    }

    private static JsonElement[] ReadSamples()
    {
        var path = Path.Combine(GetRepositoryRoot(), "samples", "samples.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceLensAnalyzer();
        return services.BuildServiceProvider();
    }

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}

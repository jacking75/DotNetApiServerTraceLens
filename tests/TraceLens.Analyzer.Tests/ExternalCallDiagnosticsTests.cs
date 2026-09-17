using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// "인식은 했지만 해석하지 않은 입력"을 결과에 남기는지 본다.
/// 이게 없으면 EF Core만 쓰는 프로젝트에서 DB 접근이 0건인데 규칙은 전부 "검사함"으로 보인다 —
/// CLAUDE.md §5(정직한 판정)에 어긋나는 화면이다.
/// </summary>
public sealed class ExternalCallDiagnosticsTests
{
    [Fact]
    public async Task AnalyzeAsync_RecordsUninterpretedFrameworkCalls()
    {
        await using var provider = CreateProvider();

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureProjectPath());

        var diagnostics = result.Graph.Diagnostics;
        Assert.NotNull(diagnostics);

        var namespaces = diagnostics.Calls.ExternalByNamespace.Select(group => group.Namespace).ToArray();
        Assert.Contains("Microsoft.EntityFrameworkCore", namespaces);
        Assert.Contains("System.Transactions", namespaces);

        // 알려진 프레임워크는 한계 문장으로 승격된다.
        Assert.Contains(diagnostics.Limitations, line => line.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Limitations, line => line.Contains("System.Transactions", StringComparison.Ordinal));

        // 집계는 해석률의 분모를 바꾸지 않는다.
        Assert.Equal(diagnostics.Calls.Resolved + diagnostics.Calls.Ambiguous + diagnostics.Calls.Unresolved, diagnostics.Calls.Inspected);
    }

    [Fact]
    public async Task AnalyzeAsync_SaysWhyDataAccessIsEmpty()
    {
        await using var provider = CreateProvider();

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureProjectPath());

        var data = result.Graph.DataAccess;
        Assert.NotNull(data);
        Assert.Empty(data.Accesses);
        Assert.Contains(data.Notes, note => note.Contains("DbContext", StringComparison.Ordinal));
        // 규칙은 실제로 돌았다. notes가 규칙 결과를 바꾸면 안 된다.
        Assert.NotEmpty(data.Rules);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceLensAnalyzer();
        return services.BuildServiceProvider();
    }

    private static string GetFixtureProjectPath() => Path.Combine(
        GetTestsRoot(), "FixturesExternal", "FixtureExternalApp", "FixtureExternalApp.csproj");

    private static string GetTestsRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetDirectoryName(sourcePath)!;
}

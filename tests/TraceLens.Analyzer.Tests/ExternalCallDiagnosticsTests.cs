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

    /// <summary>
    /// EF Core + Dapper 혼용이 실전에서 가장 흔하다.
    /// 예전에는 접근이 0건일 때만 notes를 남겨서, SQL 리터럴 1건이 있으면
    /// EF Core `DbSet`이 몇 곳이든 "EF Core는 안 읽었다"가 사라졌다(T55).
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_SaysWhatItDidNotRead_EvenWhenSomeAccessesExist()
    {
        await using var provider = CreateProvider();

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureProjectPath());

        var data = result.Graph.DataAccess;
        Assert.NotNull(data);
        Assert.NotEmpty(data.Accesses);
        Assert.Contains(data.Notes, note => note.Contains("DbContext", StringComparison.Ordinal));
        // 개수를 함께 적어야 "몇 곳을 못 봤는지"를 알 수 있다.
        Assert.Contains(data.Notes, note => note.Contains("곳", StringComparison.Ordinal));
        // 규칙은 실제로 돌았다. notes가 규칙 결과를 바꾸면 안 된다.
        Assert.NotEmpty(data.Rules);
    }

    /// <summary>프로퍼티 getter·접근자 본문 안의 쿼리도 접근으로 잡아야 한다(T55).</summary>
    [Fact]
    public async Task AnalyzeAsync_FindsAccessesInPropertyBodies()
    {
        await using var provider = CreateProvider();

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureProjectPath());

        var targets = result.Graph.DataAccess!.Accesses.Select(item => item.Target).ToArray();

        Assert.Contains("pending_orders", targets);
        Assert.Contains("archived_orders", targets);
    }

    /// <summary>
    /// 상한에 걸려 잘린 것이 로그에만 남으면 화면과 에이전트는 "그게 전부"라고 읽는다(T58).
    /// 해석률의 분자도 실제로 엣지가 된 것만 세야 한다.
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_PutsTruncationsInDiagnostics()
    {
        await using var provider = CreateProvider();

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureProjectPath());
        var diagnostics = result.Graph.Diagnostics!;

        // 분자는 그래프에 들어간 것만 센다.
        Assert.Equal(diagnostics.Calls.Resolved - diagnostics.Calls.DroppedEdges, diagnostics.Calls.ResolvedInGraph);
        Assert.Equal(diagnostics.Calls.DroppedEdges, diagnostics.DroppedEdges);

        // 솔루션 전체 스캔에서는 깊이 제한이 애초에 적용되지 않는다. 진단에 실으면 오해를 만든다.
        Assert.Equal("solution", diagnostics.AnalysisScope);
        Assert.False(diagnostics.DepthLimitReached);
        Assert.DoesNotContain(diagnostics.Limitations, line => line.Contains("maxCallDepth", StringComparison.Ordinal));
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

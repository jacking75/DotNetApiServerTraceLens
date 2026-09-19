using TraceLens.Cli.Commands;
using TraceLens.Context;
using Xunit;

namespace TraceLens.Cli.Tests;

/// <summary>
/// `impact`·`context`·`compare`가 서버 없이 저장된 분석 결과만으로 동작하는지 확인한다(T63).
///
/// 이 세 명령은 CI 스크립트가 종료 코드로 분기한다. 종료 코드가 바뀌면 조용히 잘못된 분기를 타므로
/// 코드값 자체를 확인한다.
/// </summary>
public sealed class GraphCommandsTests
{
    [Fact]
    public async Task Impact_FindsEndpointsFromDisplayName()
    {
        await using var fixture = await GraphFixture.CreateAsync();
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await GraphCommands.ImpactAsync(
            [fixture.TargetPath, "--node", "OrderService.Save"], output, error);

        Assert.Equal(GraphCommands.Success, code);
        Assert.Contains("/orders", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>PR 자동화가 쓰는 입력이다. 파일 경로가 노드로 풀리지 않으면 영향이 늘 0건이 된다.</summary>
    [Fact]
    public async Task Impact_ResolvesChangedFilesToNodes()
    {
        await using var fixture = await GraphFixture.CreateAsync();
        var output = new StringWriter();

        var code = await GraphCommands.ImpactAsync(
            [fixture.TargetPath, "--changed-files", "src/OrderService.cs", "--json"], output, new StringWriter());

        Assert.Equal(GraphCommands.Success, code);
        Assert.Contains("\"/orders\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Impact_WithoutTarget_ReturnsBadArguments()
    {
        var error = new StringWriter();

        var code = await GraphCommands.ImpactAsync(["--node", "X"], new StringWriter(), error);

        Assert.Equal(GraphCommands.BadArguments, code);
    }

    [Fact]
    public async Task Impact_WithoutNode_ReturnsBadArguments()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var code = await GraphCommands.ImpactAsync([fixture.TargetPath], new StringWriter(), new StringWriter());

        Assert.Equal(GraphCommands.BadArguments, code);
    }

    /// <summary>분석 결과가 없으면 "찾지 못했다"이지 인자 오류가 아니다. 자동화가 둘을 다르게 처리한다.</summary>
    [Fact]
    public async Task Impact_WithoutAnalysis_ReturnsFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var error = new StringWriter();
            var code = await GraphCommands.ImpactAsync(
                [Path.Combine(directory, "None.sln"), "--node", "X"], new StringWriter(), error);

            Assert.Equal(GraphCommands.Failure, code);
            Assert.Contains("tracelens analyze", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Context_WritesMarkdownToStandardOutput()
    {
        await using var fixture = await GraphFixture.CreateAsync();
        var output = new StringWriter();

        var code = await GraphCommands.ContextAsync(
            [fixture.TargetPath, "--node", "OrdersController.Create"], output, new StringWriter());

        Assert.Equal(GraphCommands.Success, code);
        Assert.Contains("OrdersController.Create", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Context_WithUnknownKind_ReturnsBadArguments()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var code = await GraphCommands.ContextAsync(
            [fixture.TargetPath, "--kind", "nope", "--node", "X"], new StringWriter(), new StringWriter());

        Assert.Equal(GraphCommands.BadArguments, code);
    }

    [Fact]
    public async Task Compare_WithoutBaseline_ReturnsBadArguments()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var code = await GraphCommands.CompareAsync([fixture.TargetPath], new StringWriter(), new StringWriter());

        Assert.Equal(GraphCommands.BadArguments, code);
    }

    [Fact]
    public async Task Compare_WithSameAnalyzer_ReturnsSuccess()
    {
        await using var fixture = await GraphFixture.CreateAsync();
        await new SnapshotStore().SaveAsync(fixture.Graph, "변경 전");
        var output = new StringWriter();

        var code = await GraphCommands.CompareAsync(
            [fixture.TargetPath, "--baseline", "latest"], output, new StringWriter());

        Assert.Equal(GraphCommands.Success, code);
    }

    /// <summary>
    /// 분석기 버전이 다른 기준과 비교하면 "차이 없음"이 안전을 뜻하지 않는다.
    /// 종료 코드 3으로 갈라 두지 않으면 자동화가 그 차이를 못 본다(CLAUDE.md 제약 5).
    /// </summary>
    [Fact]
    public async Task Compare_WithDifferentAnalyzerVersion_ReturnsPreconditionMismatch()
    {
        await using var fixture = await GraphFixture.CreateAsync();
        await new SnapshotStore().SaveAsync(
            fixture.Graph with { SnapshotId = "snap-baseline", AnalyzerVersion = "0.1.0" }, "옛 버전");
        var output = new StringWriter();

        var code = await GraphCommands.CompareAsync(
            [fixture.TargetPath, "--baseline", "latest"], output, new StringWriter());

        Assert.Equal(GraphCommands.PreconditionMismatch, code);
        Assert.Contains("전제 조건 다름", output.ToString(), StringComparison.Ordinal);
    }
}

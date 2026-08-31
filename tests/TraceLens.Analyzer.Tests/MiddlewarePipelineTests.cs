using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// 미들웨어 보기를 <c>FixtureDataApp</c>으로 검증한다. 픽스처의 Program.cs는 일부러 순서를 틀리게 두었다
/// (UseCors가 UseRouting 앞, UseAuthorization이 UseAuthentication 앞, 매핑 뒤에 등록한 미들웨어).
/// </summary>
[Collection(FixtureDataAppCollection.Name)]
public sealed class MiddlewarePipelineTests(FixtureDataAppFixture fixture)
{
    private async Task<MiddlewarePipelineDocument> GetPipelineAsync()
    {
        var result = await fixture.Result;
        return Assert.IsType<MiddlewarePipelineDocument>(result.Graph.Middleware);
    }

    [Fact]
    public async Task Pipeline_KeepsRegistrationOrderAndSkipsHostCalls()
    {
        var pipeline = await GetPipelineAsync();

        Assert.Equal("FixtureDataApp/Program.cs", pipeline.ProgramFile);
        Assert.Equal(
        [
            "ExceptionHandlingMiddleware",
            "RequestLoggingMiddleware",
            "UseHttpsRedirection",
            "UseCors",
            "UseRouting",
            "RateLimitMiddleware",
            "UseAuthorization",
            "UseAuthentication",
            "SessionValidationMiddleware",
            "MaintenanceMiddleware",
            "MapControllers",
            "LegacyHeaderMiddleware"
        ], pipeline.Steps.Select(step => step.Name).ToArray());

        // app.Run()은 파이프라인 단계가 아니라 호스트를 띄우는 호출이다.
        Assert.DoesNotContain(pipeline.Steps, step => step.Name == "Run");
        Assert.Contains(pipeline.Steps, step => step.Name == "MapControllers" && step.IsTerminal);
        Assert.Contains(pipeline.Steps, step => step.Name == "LegacyHeaderMiddleware" && step.IsDead);
    }

    [Fact]
    public async Task Pipeline_ReadsRequestAndResponseHalvesOfCustomMiddleware()
    {
        var pipeline = await GetPipelineAsync();
        var logging = Assert.Single(pipeline.Steps, step => step.Name == "RequestLoggingMiddleware");

        Assert.Equal(MiddlewareType.Custom, logging.Type);
        Assert.Contains("Items[\"TraceId\"]", logging.Pre);
        Assert.NotNull(logging.Post);
        Assert.Contains("X-Elapsed", logging.Post!);
        Assert.Contains(logging.ContextAccesses, access =>
            access.Mode == ContextAccessMode.Write && access.Target == "Items[\"TraceId\"]");

        // 클래스 XML 주석 첫 줄을 메모로 보여준다.
        Assert.NotNull(logging.Note);
        Assert.Contains("TraceId", logging.Note!);
    }

    [Fact]
    public async Task Pipeline_FindsShortCircuitAndStoreAccess()
    {
        var pipeline = await GetPipelineAsync();
        var session = Assert.Single(pipeline.Steps, step => step.Name == "SessionValidationMiddleware");

        Assert.NotNull(session.ShortCircuit);
        Assert.Equal("401", session.ShortCircuit!.StatusCode);
        Assert.Contains(session.StoreAccesses, access => access.Contains("session:{raw}", StringComparison.Ordinal));
        Assert.Contains(session.ContextAccesses, access =>
            access.Mode == ContextAccessMode.Write && access.Target == "Items[\"UserNo\"]");

        var maintenance = Assert.Single(pipeline.Steps, step => step.Name == "MaintenanceMiddleware");
        Assert.Equal("503", maintenance.ShortCircuit?.StatusCode);
    }

    [Fact]
    public async Task Rules_FindOrderAndBodyProblems()
    {
        var pipeline = await GetPipelineAsync();
        var byRule = pipeline.Findings.ToLookup(finding => finding.RuleId);

        // 순서: UseAuthorization이 UseAuthentication보다 먼저, UseCors가 UseRouting보다 먼저다.
        Assert.Contains(byRule["MW-002"], finding => finding.What.Contains("UseAuthorization", StringComparison.Ordinal));
        Assert.Contains(byRule["MW-003"], finding => finding.What.Contains("UseCors", StringComparison.Ordinal));

        // 본문: 생성자 Scoped 주입 · 조용히 끝나는 분기 · 응답 구간 헤더 수정 · 동기 대기.
        Assert.Contains(byRule["MW-004"], finding => finding.Symbol == "RateLimitMiddleware");
        Assert.Contains(byRule["MW-005"], finding => finding.Symbol == "SessionValidationMiddleware");
        Assert.Contains(byRule["MW-006"], finding => finding.Symbol == "RequestLoggingMiddleware");
        Assert.Contains(byRule["MW-008"], finding => finding.Symbol == "MaintenanceMiddleware");
        Assert.Contains(byRule["MW-012"], finding => finding.Symbol == "LegacyHeaderMiddleware");

        // 예외 처리 미들웨어가 맨 앞이라 MW-001은 걸리지 않아야 한다.
        Assert.Empty(byRule["MW-001"]);

        Assert.All(pipeline.Findings, finding =>
            Assert.Contains(pipeline.Rules, rule => rule.Id == finding.RuleId && rule.Enabled));
        Assert.Contains(pipeline.Rules, rule => rule.Id == "MW-009" && !rule.Enabled);
    }
}

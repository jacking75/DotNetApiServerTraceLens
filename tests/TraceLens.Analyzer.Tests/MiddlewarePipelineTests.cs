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

    /// <summary>
    /// 사내 파이프라인은 대부분 <c>app.UseCompanyAuth()</c> 같은 확장 메서드로 등록한다.
    /// 예전에는 이를 프레임워크로 분류하고 "본문은 어셈블리 안에 있어 열어 볼 수 없다"는 틀린 설명을 붙였다.
    /// </summary>
    [Fact]
    public async Task Pipeline_ExpandsExtensionMethodsDefinedInTheSolution()
    {
        var pipeline = await GetPipelineAsync();

        var extension = pipeline.Steps.Single(step => step.Name == "UseFixtureAuth");
        Assert.Equal(MiddlewareType.Extension, extension.Type);
        Assert.Null(extension.ParentId);
        Assert.DoesNotContain("어셈블리 안", extension.Pre ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("솔루션 안 확장 메서드", extension.Pre ?? string.Empty, StringComparison.Ordinal);

        // 하위 미들웨어가 펼쳐지고, 단락 판정은 펼친 하위 기준으로 이뤄진다.
        var apiKey = pipeline.Steps.Single(step => step.Name == "ApiKeyMiddleware");
        Assert.Equal(extension.Id, apiKey.ParentId);
        Assert.Equal(MiddlewareType.Custom, apiKey.Type);
        Assert.Equal("401", apiKey.ShortCircuit?.StatusCode);
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
            "UseFixtureAuth",
            "ApiKeyMiddleware",
            "SessionValidationMiddleware",
            "MaintenanceMiddleware",
            "MapControllers",
            "LegacyHeaderMiddleware",
            "UseEndpoints",
            "DeadHeaderMiddleware"
        ], pipeline.Steps.Select(step => step.Name).ToArray());

        // app.Run()은 파이프라인 단계가 아니라 호스트를 띄우는 호출이다.
        Assert.DoesNotContain(pipeline.Steps, step => step.Name == "Run");

        // MapControllers는 엔드포인트 "등록"이지 종단이 아니다. 그 뒤 미들웨어는 정상 실행된다.
        Assert.Contains(pipeline.Steps, step =>
            step.Name == "MapControllers" && step.Kind == MiddlewareStepKind.EndpointRegistration && !step.IsTerminal);
        Assert.Contains(pipeline.Steps, step => step.Name == "LegacyHeaderMiddleware" && !step.IsDead);

        // UseEndpoints는 종단이고, 그 뒤에 등록한 미들웨어만 실행되지 않는다.
        Assert.Contains(pipeline.Steps, step =>
            step.Name == "UseEndpoints" && step.Kind == MiddlewareStepKind.Terminal && step.IsTerminal);
        Assert.Contains(pipeline.Steps, step => step.Name == "DeadHeaderMiddleware" && step.IsDead);
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
        var dead = Assert.Single(byRule["MW-012"]);
        Assert.Equal("DeadHeaderMiddleware", dead.Symbol);
        Assert.DoesNotContain(byRule["MW-012"], finding => finding.Symbol == "LegacyHeaderMiddleware");

        // 예외 처리 미들웨어가 맨 앞이라 MW-001은 걸리지 않아야 한다.
        Assert.Empty(byRule["MW-001"]);

        Assert.All(pipeline.Findings, finding =>
            Assert.Contains(pipeline.Rules, rule => rule.Id == finding.RuleId && rule.Enabled));
        Assert.Contains(pipeline.Rules, rule => rule.Id == "MW-009" && !rule.Enabled);
    }
}

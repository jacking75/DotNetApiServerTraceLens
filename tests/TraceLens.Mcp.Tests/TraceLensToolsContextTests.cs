using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;
using TraceLens.Context;
using Xunit;

namespace TraceLens.Mcp.Tests;

/// <summary>
/// 새로 추가한 목적별 도구(§9.3)를 확인한다.
/// UI와 MCP가 같은 근거를 돌려주는지 보려고 같은 <see cref="ContextBuilder"/>를 쓴다.
/// </summary>
public sealed class TraceLensToolsContextTests
{
    [Fact]
    public async Task GetAnalysisStatus_ReportsSnapshotAndFreshness()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var status = await tools.GetAnalysisStatusAsync(null, CancellationToken.None);

        Assert.Equal("snap-1", status.SnapshotId);
        Assert.Equal(GraphDocument.CurrentSchemaVersion, status.SchemaVersion);
        Assert.Equal("fresh", status.Freshness);
        Assert.True(status.HasCallSites);
    }

    /// <summary>
    /// 기대한 스냅샷과 다르면 조용히 답하지 않고 알린다.
    /// 예외 문자열이 아니라 서버와 같은 code가 와야 에이전트가 분기할 수 있다(T60).
    /// </summary>
    [Fact]
    public async Task Tools_RejectMismatchedSnapshot()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var status = await tools.GetAnalysisStatusAsync("다른-스냅샷", CancellationToken.None);

        Assert.True(status.IsError);
        Assert.Equal(ContextErrorCodes.SnapshotMismatch, status.Error!.Code);
    }

    /// <summary>없는 노드는 서버와 같은 코드(SELECTION_NOT_FOUND)로 온다.</summary>
    [Fact]
    public async Task Tools_ReturnSelectionNotFoundForUnknownNode()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var result = await tools.GetNodeAsync("없는.노드()", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ContextErrorCodes.SelectionNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task GetContext_ReturnsSameDocumentShapeAsServer()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var result = await tools.GetContextAsync(
            "node", "Fx.Service.Run()", "explain", "balanced", "무엇을 하나?", null, null, "snap-1", CancellationToken.None);

        Assert.Equal("snap-1", result.SnapshotId);
        // 기본은 Markdown 하나다. 구조 필드까지 함께 주면 같은 내용을 두 번 보낸다(T61).
        Assert.Equal("markdown", result.Format);
        Assert.Null(result.Document);
        Assert.Contains("TraceLens 문맥", result.Markdown!, StringComparison.Ordinal);
        Assert.Equal(result.Markdown!.Length, result.Characters);

        var both = await tools.GetContextAsync(
            "node", "Fx.Service.Run()", "explain", "balanced", null, "both", null, "snap-1", CancellationToken.None);
        Assert.NotNull(both.Document);
        Assert.NotNull(both.Markdown);
    }

    [Fact]
    public async Task GetEntryPointFlow_ReturnsBoundedFlow()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var flow = await tools.GetEntryPointFlowAsync("GET /orders", 3, 50, null, CancellationToken.None);

        Assert.Equal("/orders", flow.Route);
        Assert.Equal(2, flow.Nodes.Count);
        Assert.Single(flow.Edges);
    }

    [Fact]
    public async Task GetImpact_ReturnsCandidatesWithLimitations()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var result = await tools.GetImpactAsync(
            ["Fx.Service.Run()"], null, null, null, null, null, null, CancellationToken.None);

        Assert.NotNull(result.Impact);
        Assert.Single(result.Impact!.Endpoints);
        Assert.NotEmpty(result.Impact.Limitations);
    }

    /// <summary>목록 도구는 페이지 구분을 지원해서 큰 응답을 한 번에 밀어 넣지 않는다.</summary>
    [Fact]
    public async Task ListTools_SupportPaging()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var first = await tools.SearchSymbolAsync("Fx", 1, null, null, CancellationToken.None);
        var second = await tools.SearchSymbolAsync("Fx", 1, first.NextCursor, null, CancellationToken.None);

        Assert.Single(first.Items);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(1, first.NextCursor);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
    }

    /// <summary>이 스냅샷이 분석하지 않은 파일은 읽지 않는다.</summary>
    [Fact]
    public async Task GetSource_DeniesFileOutsideSnapshot()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        // 거부는 예외가 아니라 code가 있는 구조로 돌려준다. 에이전트가 사유를 구분할 수 있어야 한다.
        var result = await tools.GetSourceAsync("Src/Secret.cs", 1, 2, null, null, CancellationToken.None);

        Assert.NotNull(result.Denied);
        Assert.Equal("SOURCE_DENIED", result.Denied!.Code);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task GetFindings_ReturnsDisabledRuleIds()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var findings = await tools.GetFindingsAsync(null, null, null, null, null, CancellationToken.None);

        Assert.Single(findings.Items);
        Assert.Equal(["RULE-OFF"], findings.DisabledRuleIds);
    }

    /// <summary>
    /// 에이전트 안내문이 시키는 `ruleId=COMP-*`가 0건을 돌려주면
    /// 에이전트는 "문제 없음"이라고 답한다 — 침묵 오답이다(T60).
    /// </summary>
    [Theory]
    [InlineData("RULE-")]
    [InlineData("RULE-*")]
    [InlineData("RULE-ON")]
    [InlineData("rule-on")]
    public async Task GetFindings_MatchesRuleIdByPrefix(string filter)
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var findings = await tools.GetFindingsAsync(null, filter, null, null, null, CancellationToken.None);

        Assert.Single(findings.Items);
        Assert.Contains("RULE-ON", findings.MatchedRuleIds);
    }

    /// <summary>0건일 때 "문제 없음"과 "그 규칙을 검사하지 않았음"을 구분해 준다.</summary>
    [Fact]
    public async Task GetFindings_ExplainsEmptyResult()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var offOnly = await tools.GetFindingsAsync(null, "RULE-OFF", null, null, null, CancellationToken.None);
        Assert.Empty(offOnly.Items);
        Assert.Contains("꺼짐", offOnly.Note!);

        var unknown = await tools.GetFindingsAsync(null, "NOPE-", null, null, null, CancellationToken.None);
        Assert.Empty(unknown.Items);
        Assert.Contains("카탈로그에 없다", unknown.Note!);
    }

    /// <summary>
    /// 안내문은 "무엇을 묻든 get_analysis_status를 먼저"라고 하는데 그 호출이 솔루션 전체를 다시 해시했다(T61).
    /// 연속 호출에서는 파일을 다시 읽지 않아야 한다.
    /// </summary>
    [Fact]
    public async Task GetAnalysisStatus_ReusesFreshnessBetweenCalls()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var first = await tools.GetAnalysisStatusAsync(null, CancellationToken.None);
        var second = await tools.GetAnalysisStatusAsync(null, CancellationToken.None);

        Assert.Equal(first.FreshnessCheckedAt, second.FreshnessCheckedAt);
        Assert.Equal(0, second.FreshnessCheckDurationMs);
    }

    /// <summary>UI의 여섯 보기가 MCP에서도 열거돼야 에이전트가 같은 것을 볼 수 있다(T62).</summary>
    [Fact]
    public async Task NewTools_ExposeClassesAndSnapshots()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = CreateTools(fixture);

        var classes = await tools.ListClassesAsync(null, null, null, null, null, CancellationToken.None);
        Assert.Contains(classes.Items, item => item.Id == "Fx.Service");

        var single = await tools.GetClassAsync("Fx.Service", null, CancellationToken.None);
        Assert.NotNull(single.Class);
        // get_context에 넘길 노드 ID가 함께 와야 막다른 길이 되지 않는다.
        Assert.Contains(single.Nodes, node => node.Id == "Fx.Service.Run()");

        var snapshots = await tools.ListSnapshotsAsync(null, CancellationToken.None);
        Assert.False(snapshots.IsError);

        var writePaths = await tools.ListWritePathsAsync(null, null, null, null, CancellationToken.None);
        Assert.False(writePaths.IsError);

        // 미들웨어·시작 흐름이 없는 픽스처에서는 사유가 있는 오류로 온다(조용히 빈 결과를 주지 않는다).
        var pipeline = await tools.GetMiddlewarePipelineAsync(null, null, null, null, CancellationToken.None);
        Assert.True(pipeline.IsError);
        Assert.Equal(ContextErrorCodes.SelectionNotFound, pipeline.Error!.Code);
    }

    private static TraceLensTools CreateTools(ContextFixture fixture) =>
        new(new GraphFileStore(fixture.GraphPath), NullLogger<TraceLensTools>.Instance);

    internal sealed class ContextFixture(string directory, string graphPath) : IAsyncDisposable
    {
        public string GraphPath { get; } = graphPath;

        public static async Task<ContextFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "tracelens-mcp-context", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "Src"));
            var controllerPath = Path.Combine(directory, "Src", "Controller.cs");
            var servicePath = Path.Combine(directory, "Src", "Service.cs");
            await File.WriteAllTextAsync(controllerPath, "void Get()\n{\n}\n");
            await File.WriteAllTextAsync(servicePath, "void Run()\n{\n}\n");

            var controller = new GraphNode(
                "Fx.Controller.Get()", NodeKind.Controller, "Controller.Get", "Fx", "Fx.Controller", "Src/Controller.cs", 1, 3)
            {
                StableKey = "Fx::Fx.Controller.Get()"
            };
            var service = new GraphNode(
                "Fx.Service.Run()", NodeKind.Service, "Service.Run", "Fx", "Fx.Service", "Src/Service.cs", 1, 3)
            {
                StableKey = "Fx::Fx.Service.Run()"
            };
            var finding = new Finding(
                "f1", "RULE-ON", FindingSeverity.Warning, FindingConfidence.Certain,
                "무엇", "왜", "고치는 법", "Service.Run", "Src/Service.cs", 1, service.Id, null, null, null, []);

            var graph = new GraphDocument(
                GraphDocument.CurrentSchemaVersion,
                Path.Combine(directory, "Fx.sln"),
                DateTimeOffset.Parse("2026-09-08T10:00:00+09:00"),
                [
                    new EntryPoint(controller.Id, "GET", "/orders")
                    {
                        EndpointId = "Fx|GET|/orders|Fx.Controller.Get()",
                        RegistrationFile = "Src/Controller.cs",
                        RegistrationLine = 1
                    }
                ],
                [new GraphClass("Fx.Service", "Service", "Fx", NodeKind.Service, "Src/Service.cs", 1, 3, [service.Id])],
                [controller, service],
                [new GraphEdge(controller.Id, service.Id)],
                new Dictionary<string, string>
                {
                    ["Src/Controller.cs"] = await SnapshotHasher.ComputeFileAsync(controllerPath),
                    ["Src/Service.cs"] = await SnapshotHasher.ComputeFileAsync(servicePath)
                })
            {
                RootDirectory = directory,
                SnapshotId = "snap-1",
                InputFingerprint = "sha256:fixture",
                AnalyzerVersion = "1.0.0",
                RulesVersion = "1",
                Manifest = new AnalysisManifest([], ["Src"], AnalysisManifestBuilder.ExcludedDirectories, [], []),
                CallSites =
                [
                    new CallSite(controller.Id, service.Id, "Fx.IService.Run()", "Src/Controller.cs", 2,
                        CallResolution.DiRegistration, CallConfidence.Certain)
                ],
                Diagnostics = AnalysisDiagnostics.Empty with { AnalysisScope = "solution" },
                DataAccess = new DataAccessDocument(
                    false, [], [], [], [], [finding],
                    [
                        new RuleInfo("RULE-ON", "g", "켜짐", FindingSeverity.Warning, FindingConfidence.Certain, "d", false, true),
                        new RuleInfo("RULE-OFF", "g", "꺼짐", FindingSeverity.Info, FindingConfidence.Certain, "d", false, false)
                    ])
            };

            var graphDirectory = Path.Combine(directory, ".tracelens");
            Directory.CreateDirectory(graphDirectory);
            var graphPath = Path.Combine(graphDirectory, "graph.json");
            await File.WriteAllTextAsync(graphPath, JsonSerializer.Serialize(graph, GraphDocument.JsonOptions));
            return new ContextFixture(directory, graphPath);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }

            return ValueTask.CompletedTask;
        }
    }
}

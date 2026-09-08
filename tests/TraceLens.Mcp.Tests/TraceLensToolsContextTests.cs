using System.Text.Json;
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
        var tools = new TraceLensTools(new GraphFileStore(fixture.GraphPath));

        var status = await tools.GetAnalysisStatusAsync(null, CancellationToken.None);

        Assert.Equal("snap-1", status.SnapshotId);
        Assert.Equal(GraphDocument.CurrentSchemaVersion, status.SchemaVersion);
        Assert.Equal("fresh", status.Freshness);
        Assert.True(status.HasCallSites);
    }

    /// <summary>기대한 스냅샷과 다르면 조용히 답하지 않고 알린다.</summary>
    [Fact]
    public async Task Tools_RejectMismatchedSnapshot()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = new TraceLensTools(new GraphFileStore(fixture.GraphPath));

        var exception = await Assert.ThrowsAsync<ContextException>(
            () => tools.GetAnalysisStatusAsync("다른-스냅샷", CancellationToken.None));

        Assert.Equal("SNAPSHOT_MISMATCH", exception.Code);
    }

    [Fact]
    public async Task GetContext_ReturnsSameDocumentShapeAsServer()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = new TraceLensTools(new GraphFileStore(fixture.GraphPath));

        var document = await tools.GetContextAsync(
            "node", "Fx.Service.Run()", "explain", "balanced", "무엇을 하나?", "snap-1", CancellationToken.None);

        Assert.Equal("snap-1", document.SnapshotId);
        Assert.Equal("무엇을 하나?", document.Question);
        Assert.Contains("TraceLens 문맥", document.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEntryPointFlow_ReturnsBoundedFlow()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = new TraceLensTools(new GraphFileStore(fixture.GraphPath));

        var flow = await tools.GetEntryPointFlowAsync("GET /orders", 3, 50, null, CancellationToken.None);

        Assert.Equal("/orders", flow.Route);
        Assert.Equal(2, flow.Nodes.Count);
        Assert.Single(flow.Edges);
    }

    [Fact]
    public async Task GetImpact_ReturnsCandidatesWithLimitations()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = new TraceLensTools(new GraphFileStore(fixture.GraphPath));

        var impact = await tools.GetImpactAsync(["Fx.Service.Run()"], null, null, null, CancellationToken.None);

        Assert.Single(impact.Endpoints);
        Assert.NotEmpty(impact.Limitations);
    }

    /// <summary>목록 도구는 페이지 구분을 지원해서 큰 응답을 한 번에 밀어 넣지 않는다.</summary>
    [Fact]
    public async Task ListTools_SupportPaging()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = new TraceLensTools(new GraphFileStore(fixture.GraphPath));

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
        var tools = new TraceLensTools(new GraphFileStore(fixture.GraphPath));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.GetSourceAsync("Src/Secret.cs", 1, 2, null, CancellationToken.None));
    }

    [Fact]
    public async Task GetFindings_ReturnsDisabledRuleIds()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var tools = new TraceLensTools(new GraphFileStore(fixture.GraphPath));

        var findings = await tools.GetFindingsAsync(null, null, null, null, null, CancellationToken.None);

        Assert.Single(findings.Items);
        Assert.Equal(["RULE-OFF"], findings.DisabledRuleIds);
    }

    private sealed class ContextFixture(string directory, string graphPath) : IAsyncDisposable
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

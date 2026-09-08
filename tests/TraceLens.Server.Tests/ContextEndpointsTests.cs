using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using TraceLens.Analyzer.Models;
using TraceLens.Context;
using TraceLens.Server;
using TraceLens.Server.Endpoints;
using Xunit;

namespace TraceLens.Server.Tests;

public sealed class ContextEndpointsTests
{
    [Fact]
    public async Task BuildAsync_ReturnsNotFound_WhenNothingAnalyzed()
    {
        var result = await ContextEndpoints.BuildAsync(
            new ContextApiRequest("Node", "X"),
            new GraphStore(),
            NewBuilder(),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task BuildAsync_RejectsUnknownSelectionKind()
    {
        var store = new GraphStore();
        store.Set(Fixtures.Graph());

        var result = await ContextEndpoints.BuildAsync(
            new ContextApiRequest("banana", "X"),
            store,
            NewBuilder(),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    /// <summary>기대한 스냅샷과 다르면 409로 알려서 오래된 결과를 조용히 쓰지 않게 한다.</summary>
    [Fact]
    public async Task BuildAsync_ReturnsConflict_OnSnapshotMismatch()
    {
        var store = new GraphStore();
        store.Set(Fixtures.Graph());

        var result = await ContextEndpoints.BuildAsync(
            new ContextApiRequest("Node", "Fx.Service.Run()") { ExpectedSnapshotId = "다른-스냅샷" },
            store,
            NewBuilder(),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }

    [Fact]
    public async Task BuildAsync_ReturnsContextWithMarkdown()
    {
        var store = new GraphStore();
        store.Set(Fixtures.Graph());

        var result = await ContextEndpoints.BuildAsync(
            new ContextApiRequest("Node", "Fx.Service.Run()") { IncludeSource = false },
            store,
            NewBuilder(),
            CancellationToken.None);

        var document = Assert.IsType<Ok<ContextDocument>>(result).Value;
        Assert.NotNull(document);
        Assert.Equal("snap-1", document.SnapshotId);
        Assert.False(document.ContainsSource);
        Assert.Contains("TraceLens 문맥", document.Markdown, StringComparison.Ordinal);
    }

    /// <summary>공유 보고서에는 소스 본문이 들어가지 않는다.</summary>
    [Fact]
    public async Task BuildReportAsync_ExcludesSource()
    {
        var store = new GraphStore();
        store.Set(Fixtures.Graph());

        var result = await ContextEndpoints.BuildReportAsync(
            new ReportApiRequest("Node", "Fx.Service.Run()"),
            store,
            NewBuilder(),
            new ShareReportBuilder(),
            CancellationToken.None);

        var report = Assert.IsType<Ok<ShareReport>>(result).Value;
        Assert.NotNull(report);
        Assert.DoesNotContain("```csharp", report.Markdown, StringComparison.Ordinal);
        Assert.Contains("한계", report.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImpactAsync_RequiresNodeIds()
    {
        var store = new GraphStore();
        store.Set(Fixtures.Graph());

        var result = await ImpactEndpoints.AnalyzeAsync(
            new ImpactApiRequest([]), store, new ImpactAnalyzer(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task ImpactAsync_ReturnsAffectedEndpoints()
    {
        var store = new GraphStore();
        store.Set(Fixtures.Graph());

        var result = await ImpactEndpoints.AnalyzeAsync(
            new ImpactApiRequest(["Fx.Service.Run()"]), store, new ImpactAnalyzer(), CancellationToken.None);

        var impact = Assert.IsType<Ok<ImpactResult>>(result).Value;
        Assert.NotNull(impact);
        Assert.Single(impact.Endpoints);
        Assert.Equal("/orders", impact.Endpoints[0].Route);
    }

    /// <summary>가져온 분석 결과는 기준으로 저장하지 않는다.</summary>
    [Fact]
    public async Task SaveSnapshotAsync_RejectsOfflineSnapshot()
    {
        var store = new GraphStore();
        store.Set(Fixtures.Graph(), GraphOrigin.OfflineSnapshot, "C:\\temp\\graph.json");

        var result = await SnapshotEndpoints.SaveAsync(
            new SaveSnapshotRequest("변경 전"), store, new SnapshotStore(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    /// <summary>가져온 그래프에서는 소스를 읽지 않는다(rootDirectory만 믿고 파일을 열지 않는다).</summary>
    [Fact]
    public void OfflineSnapshot_DeniesSourceAccess()
    {
        var store = new GraphStore();
        store.Set(Fixtures.Graph(), GraphOrigin.OfflineSnapshot);

        var access = store.Session!.Source.Policy.Check("Src/Service.cs");

        Assert.False(access.Allowed);
        Assert.Equal(SourceAccessDenial.OutsideRoot, access.Denial);
    }

    private static ContextBuilder NewBuilder() => new(new GraphFreshnessEvaluator());

    private static int StatusOf(IResult result) => result switch
    {
        IStatusCodeHttpResult statusCode => statusCode.StatusCode ?? StatusCodes.Status200OK,
        _ => StatusCodes.Status200OK
    };

    private static class Fixtures
    {
        public static GraphDocument Graph()
        {
            var controller = Node("Fx.Controller.Get()", "Controller.Get", NodeKind.Controller);
            var service = Node("Fx.Service.Run()", "Service.Run", NodeKind.Service);
            return new GraphDocument(
                GraphDocument.CurrentSchemaVersion,
                "C:\\Fx\\Fx.sln",
                DateTimeOffset.Parse("2026-09-08T10:00:00+09:00"),
                [
                    new EntryPoint(controller.Id, "GET", "/orders")
                    {
                        EndpointId = "Fx|GET|/orders|Fx.Controller.Get()",
                        RegistrationFile = "Src/Controller.cs",
                        RegistrationLine = 1,
                    }
                ],
                [new GraphClass("Fx.Service", "Service", "Fx", NodeKind.Service, "Src/Service.cs", 1, 3, [service.Id])],
                [controller, service],
                [new GraphEdge(controller.Id, service.Id)],
                new Dictionary<string, string>())
            {
                RootDirectory = "C:\\Fx",
                SnapshotId = "snap-1",
                InputFingerprint = "sha256:fixture",
                AnalyzerVersion = "1.0.0",
                RulesVersion = "1",
                Diagnostics = AnalysisDiagnostics.Empty with { AnalysisScope = "solution" }
            };
        }

        private static GraphNode Node(string id, string displayName, NodeKind kind) =>
            new(id, kind, displayName, "Fx", id[..id.LastIndexOf('.')], "Src/Service.cs", 1, 3)
            {
                StableKey = "Fx::" + id
            };
    }
}

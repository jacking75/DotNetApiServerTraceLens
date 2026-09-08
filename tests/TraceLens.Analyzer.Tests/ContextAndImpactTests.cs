using TraceLens.Analyzer.Models;
using TraceLens.Context;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// 문맥·영향·비교(§9~§11)를 고정 그래프로 확인한다.
/// Roslyn을 돌리지 않고 그래프 계약만 보므로 UI와 MCP가 같은 근거를 돌려주는지 확인하기 좋다.
/// </summary>
public sealed class ContextAndImpactTests
{
    [Fact]
    public async Task ContextBuilder_IncludesFreshnessPathsAndLimitations()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var document = await fixture.BuildContextAsync(
            new ContextSelection(SelectionKind.Node, "Fx.Repository.Save(int)"),
            ContextIntent.PlanChange);

        Assert.Equal(ContextDocument.Version, document.ContextVersion);
        Assert.Equal(fixture.Graph.SnapshotId, document.SnapshotId);
        Assert.Equal("fresh", document.Freshness.Status);
        var path = Assert.Single(document.Paths);
        Assert.Equal("GET", path.HttpMethod);
        Assert.Equal(["Fx.Controller.Get()", "Fx.Service.Run()", "Fx.Repository.Save(int)"],
            path.Steps.Select(step => step.NodeId).ToArray());
        Assert.Contains(document.Evidence, item => item.Kind == "declaration");
        Assert.NotEmpty(document.Limitations);
        Assert.Contains("정적 분석", document.Markdown, StringComparison.Ordinal);
    }

    /// <summary>같은 스냅샷·선택·예산이면 언제 만들어도 같은 문맥이 나와야 한다(UI와 MCP가 같은 근거를 본다).</summary>
    [Fact]
    public async Task ContextBuilder_IsDeterministicForSameSelectionAndBudget()
    {
        await using var fixture = await GraphFixture.CreateAsync();
        var selection = new ContextSelection(SelectionKind.Node, "Fx.Service.Run()");

        var first = await fixture.BuildContextAsync(selection, ContextIntent.Explain);
        var second = await fixture.BuildContextAsync(selection, ContextIntent.Explain);

        Assert.Equal(first.Markdown, second.Markdown);
    }

    /// <summary>클래스만 골라도 문맥을 만들 수 있어야 한다(실행 가능한 nodeId가 없어도 된다).</summary>
    [Fact]
    public async Task ContextBuilder_SupportsClassSelectionWithoutRunnableNode()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var document = await fixture.BuildContextAsync(
            new ContextSelection(SelectionKind.Class, "Fx.Service"),
            ContextIntent.Explain);

        Assert.Contains("Fx.Service", document.Summary, StringComparison.Ordinal);
        Assert.NotEmpty(document.Evidence);
    }

    [Fact]
    public async Task ContextBuilder_RejectsMismatchedSnapshot()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ContextException>(() => fixture.Builder.BuildAsync(
            fixture.Index,
            fixture.Source,
            new ContextRequest(new ContextSelection(SelectionKind.Node, "Fx.Service.Run()"))
            {
                ExpectedSnapshotId = "다른-스냅샷"
            }));

        Assert.Equal("SNAPSHOT_MISMATCH", exception.Code);
    }

    /// <summary>긴 본문은 상한 안에 담고 생략한 범위를 명시해야 한다.</summary>
    [Fact]
    public async Task ContextBuilder_TruncatesLongSourceAndReportsOmission()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var document = await fixture.BuildContextAsync(
            new ContextSelection(SelectionKind.Node, "Fx.Repository.Save(int)"),
            ContextIntent.Explain,
            ContextBudget.Short);

        Assert.True(document.Markdown.Length <= 4_000);
        Assert.True(document.Truncated || document.Omitted.SourceRanges.Count > 0);
    }

    [Fact]
    public async Task ImpactAnalyzer_FindsAffectedEndpointsWithEvidencePath()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var result = new ImpactAnalyzer().Analyze(fixture.Index, new ImpactRequest(["Fx.Repository.Save(int)"]));

        var endpoint = Assert.Single(result.Endpoints);
        Assert.Equal("/orders", endpoint.Route);
        Assert.Equal(2, endpoint.Distance);
        Assert.Equal(["Fx.Controller.Get()", "Fx.Service.Run()", "Fx.Repository.Save(int)"],
            endpoint.Path.Select(step => step.NodeId).ToArray());
        Assert.Contains(result.Limitations, item => item.Contains("삭제해도 안전", StringComparison.Ordinal));
    }

    /// <summary>도달하는 진입점이 없으면 "안전"이 아니라 "도달 못 함"으로 보고해야 한다.</summary>
    [Fact]
    public async Task ImpactAnalyzer_ReportsUnreachableTargetsInsteadOfSafety()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var result = new ImpactAnalyzer().Analyze(fixture.Index, new ImpactRequest(["Fx.Orphan.Run()"]));

        Assert.Empty(result.Endpoints);
        Assert.Equal(["Fx.Orphan.Run()"], result.UnreachableTargets);
    }

    /// <summary>순환 호출이 있어도 멈춰야 한다.</summary>
    [Fact]
    public async Task ImpactAnalyzer_HandlesCycles()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var result = new ImpactAnalyzer().Analyze(fixture.Index, new ImpactRequest(["Fx.Cycle.B()"]));

        Assert.Empty(result.Endpoints);
        Assert.True(result.VisitedNodes > 0);
    }

    [Fact]
    public async Task ImpactAnalyzer_StopsAtDepthLimitAndSaysWhy()
    {
        await using var fixture = await GraphFixture.CreateAsync();

        var result = new ImpactAnalyzer().Analyze(
            fixture.Index,
            new ImpactRequest(["Fx.Repository.Save(int)"], MaxDepth: 1));

        Assert.True(result.Truncated);
        Assert.Contains(result.TruncationReasons, reason => reason.Contains("깊이 상한", StringComparison.Ordinal));
    }

    /// <summary>공백만 바뀐 변경은 본문 지문이 같아야 한다.</summary>
    [Fact]
    public void SnapshotComparer_IgnoresWhitespaceOnlyChangeAndFlagsBodyChange()
    {
        var baseline = GraphFixture.CreateGraph("snap-1");
        var whitespaceOnly = baseline with { SnapshotId = "snap-2" };
        var bodyChanged = baseline with
        {
            SnapshotId = "snap-3",
            Nodes = baseline.Nodes
                .Select(node => node.Id == "Fx.Service.Run()" ? node with { BodyFingerprint = "sha256:changed" } : node)
                .ToArray()
        };

        var comparer = new SnapshotComparer();
        var same = comparer.Compare(baseline, whitespaceOnly);
        var changed = comparer.Compare(baseline, bodyChanged);

        Assert.Empty(same.Methods);
        Assert.Contains(changed.Methods, item => item.Kind == DiffKind.Changed && item.Detail.Contains("본문", StringComparison.Ordinal));
    }

    [Fact]
    public void SnapshotComparer_ReportsRouteAndEdgeChanges()
    {
        var baseline = GraphFixture.CreateGraph("snap-1");
        var current = baseline with
        {
            SnapshotId = "snap-2",
            EntryPoints = [],
            Edges = baseline.Edges.Where(edge => edge.To != "Fx.Repository.Save(int)").ToArray()
        };

        var diff = new SnapshotComparer().Compare(baseline, current);

        Assert.Contains(diff.Endpoints, item => item.Kind == DiffKind.Removed);
        Assert.Contains(diff.Edges, item => item.Kind == DiffKind.Removed);
        Assert.Contains(diff.Limitations, item => item.Contains("안전", StringComparison.Ordinal));
    }

    /// <summary>규칙이 꺼져서 사라진 점검을 "수정 완료"로 보면 안 된다.</summary>
    [Fact]
    public void SnapshotComparer_MarksFindingsLostByDisabledRuleAsNotComparable()
    {
        var baseline = GraphFixture.CreateGraph("snap-1");
        var current = baseline with
        {
            SnapshotId = "snap-2",
            DataAccess = baseline.DataAccess! with
            {
                Findings = [],
                Rules = [baseline.DataAccess!.Rules[0] with { Enabled = false }]
            }
        };

        var diff = new SnapshotComparer().Compare(baseline, current);

        Assert.Contains(diff.Findings, item => item.Kind == DiffKind.NotComparable);
        Assert.Contains(diff.Limitations, item => item.Contains("꺼진 규칙", StringComparison.Ordinal));
    }

    /// <summary>분석기·규칙·범위가 다르면 비교 조건이 다르다고 알려야 한다.</summary>
    [Fact]
    public void SnapshotComparer_FlagsDifferentPreconditions()
    {
        var baseline = GraphFixture.CreateGraph("snap-1");
        var current = baseline with { SnapshotId = "snap-2", AnalyzerVersion = "9.9.9" };

        var diff = new SnapshotComparer().Compare(baseline, current);

        Assert.False(diff.Preconditions.Ok);
        Assert.Contains(diff.Preconditions.Differences, item => item.Contains("분석기 버전", StringComparison.Ordinal));
    }

    private sealed class GraphFixture(string directory, GraphDocument graph) : IAsyncDisposable
    {
        public GraphDocument Graph { get; } = graph;

        public GraphIndex Index { get; } = new(graph);

        public SourceReader Source { get; } = new(graph, SourceAccessPolicy.ForGraph(graph));

        public ContextBuilder Builder { get; } = new(new GraphFreshnessEvaluator());

        public Task<ContextDocument> BuildContextAsync(
            ContextSelection selection,
            ContextIntent intent,
            ContextBudget budget = ContextBudget.Balanced) =>
            Builder.BuildAsync(Index, Source, new ContextRequest(selection, intent, budget));

        public static async Task<GraphFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "tracelens-context-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "Src"));
            var repositoryPath = Path.Combine(directory, "Src", "Repository.cs");
            await File.WriteAllTextAsync(
                repositoryPath,
                string.Join(Environment.NewLine, Enumerable.Range(1, 400).Select(line => $"// 줄 {line}")));
            var servicePath = Path.Combine(directory, "Src", "Service.cs");
            await File.WriteAllTextAsync(servicePath, "public void Run() { }\n");
            var controllerPath = Path.Combine(directory, "Src", "Controller.cs");
            await File.WriteAllTextAsync(controllerPath, "public void Get() { }\n");

            var graph = CreateGraph("snap-test") with
            {
                SolutionPath = Path.Combine(directory, "Fx.sln"),
                RootDirectory = directory,
                FileHashes = new Dictionary<string, string>
                {
                    ["Src/Repository.cs"] = await SnapshotHasher.ComputeFileAsync(repositoryPath),
                    ["Src/Service.cs"] = await SnapshotHasher.ComputeFileAsync(servicePath),
                    ["Src/Controller.cs"] = await SnapshotHasher.ComputeFileAsync(controllerPath)
                },
                Manifest = new AnalysisManifest([], ["Src"], AnalysisManifestBuilder.ExcludedDirectories, [], [])
            };
            return new GraphFixture(directory, graph);
        }

        public static GraphDocument CreateGraph(string snapshotId)
        {
            var controller = Node("Fx.Controller.Get()", "Controller.Get", "Src/Controller.cs", 1, 1, NodeKind.Controller);
            var service = Node("Fx.Service.Run()", "Service.Run", "Src/Service.cs", 1, 1, NodeKind.Service);
            var repository = Node("Fx.Repository.Save(int)", "Repository.Save", "Src/Repository.cs", 1, 400, NodeKind.Repository);
            var orphan = Node("Fx.Orphan.Run()", "Orphan.Run", "Src/Service.cs", 1, 1, NodeKind.Method);
            var cycleA = Node("Fx.Cycle.A()", "Cycle.A", "Src/Service.cs", 1, 1, NodeKind.Method);
            var cycleB = Node("Fx.Cycle.B()", "Cycle.B", "Src/Service.cs", 1, 1, NodeKind.Method);

            var finding = new Finding(
                "f1", "COMP-002", FindingSeverity.Critical, FindingConfidence.Certain,
                "되돌아가지 않는다", "왜", "고치는 법", "Service.Run", "Src/Service.cs", 1,
                service.Id, null, null, null, []);
            var rule = new RuleInfo("COMP-002", "보상", "보상 누락", FindingSeverity.Critical, FindingConfidence.Certain, "설명", false, true);

            return new GraphDocument(
                GraphDocument.CurrentSchemaVersion,
                "C:\\Fx\\Fx.sln",
                DateTimeOffset.Parse("2026-09-08T10:00:00+09:00"),
                [
                    new EntryPoint(controller.Id, "GET", "/orders")
                    {
                        EndpointId = "Fx|GET|/orders|Fx.Controller.Get()",
                        NormalizedRoute = "/orders",
                        RegistrationFile = "Src/Controller.cs",
                        RegistrationLine = 1,
                        HandlerDisplayName = controller.DisplayName
                    }
                ],
                [
                    new GraphClass("Fx.Service", "Service", "Fx", NodeKind.Service, "Src/Service.cs", 1, 1, [service.Id])
                ],
                [controller, service, repository, orphan, cycleA, cycleB],
                [
                    new GraphEdge(controller.Id, service.Id),
                    new GraphEdge(service.Id, repository.Id),
                    new GraphEdge(cycleA.Id, cycleB.Id),
                    new GraphEdge(cycleB.Id, cycleA.Id)
                ],
                new Dictionary<string, string>())
            {
                SnapshotId = snapshotId,
                InputFingerprint = "sha256:fixture",
                AnalyzerVersion = "1.0.0",
                RulesVersion = "1",
                CallSites =
                [
                    new CallSite(controller.Id, service.Id, "Fx.IService.Run()", "Src/Controller.cs", 1,
                        CallResolution.DiRegistration, CallConfidence.Certain),
                    new CallSite(service.Id, repository.Id, "Fx.IRepository.Save(int)", "Src/Service.cs", 1,
                        CallResolution.Direct, CallConfidence.Certain)
                ],
                Diagnostics = AnalysisDiagnostics.Empty with
                {
                    TotalProjects = 1,
                    LoadedProjects = 1,
                    AnalysisScope = "solution",
                    Calls = new CallResolutionStats(2, 2, 1, 1, 0, 0, 0, 5)
                },
                DataAccess = new DataAccessDocument(false, [], [], [], [], [finding], [rule])
            };
        }

        private static GraphNode Node(string id, string displayName, string file, int start, int end, NodeKind kind) =>
            new(id, kind, displayName, "Fx", id[..id.LastIndexOf('.')], file, start, end)
            {
                StableKey = "Fx::" + id,
                SignatureFingerprint = "sha256:sig-" + id,
                BodyFingerprint = "sha256:body-" + id
            };

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

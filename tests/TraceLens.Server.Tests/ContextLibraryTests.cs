using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;
using TraceLens.Context;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// Context 라이브러리 중 테스트가 없던 곳(신선도 판정 경로 · 환경 점검 · 공유 보고서 · 기준 보관)을 채운다.
/// </summary>
public sealed class ContextLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tracelens-ctx-" + Guid.NewGuid().ToString("N"));

    public ContextLibraryTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Src"));
        File.WriteAllText(Path.Combine(root, "Src", "Service.cs"), "class Service { }");
        File.WriteAllText(Path.Combine(root, "tracelens.config.json"), "{ }");
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    /* ---------------------------------------------------------------- 신선도 판정 경로 */

    [Fact]
    public async Task Evaluate_ReturnsFresh_WhenNothingChanged()
    {
        var report = await new GraphFreshnessEvaluator().EvaluateAsync(await CreateGraphAsync());

        Assert.Equal(FreshnessStatus.Fresh, report.Status);
        Assert.Empty(report.Changes);
    }

    [Fact]
    public async Task Evaluate_ReportsModified()
    {
        var graph = await CreateGraphAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "Src", "Service.cs"), "class Service { int x; }");

        var report = await new GraphFreshnessEvaluator().EvaluateAsync(graph);

        Assert.Equal(FreshnessStatus.Stale, report.Status);
        Assert.Contains(report.Changes, item => item.Kind == FreshnessChangeKind.Modified);
    }

    [Fact]
    public async Task Evaluate_ReportsDeleted()
    {
        var graph = await CreateGraphAsync();
        File.Delete(Path.Combine(root, "Src", "Service.cs"));

        var report = await new GraphFreshnessEvaluator().EvaluateAsync(graph);

        Assert.Contains(report.Changes, item => item.Kind == FreshnessChangeKind.Deleted);
    }

    [Fact]
    public async Task Evaluate_ReportsAdded()
    {
        var graph = await CreateGraphAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "Src", "New.cs"), "class New { }");

        var report = await new GraphFreshnessEvaluator().EvaluateAsync(graph);

        Assert.Contains(report.Changes, item => item.Kind == FreshnessChangeKind.Added && item.Path.EndsWith("New.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Evaluate_ReportsConfigChanged()
    {
        var graph = await CreateGraphAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "tracelens.config.json"), """{ "maxCallDepth": 5 }""");

        var report = await new GraphFreshnessEvaluator().EvaluateAsync(graph);

        Assert.Contains(report.Changes, item => item.Kind == FreshnessChangeKind.ConfigChanged);
    }

    /// <summary>분석 당시에도 컴파일되지 않던 파일은 "새 파일"이 아니다. 그러면 영원히 stale이 된다.</summary>
    [Fact]
    public async Task Evaluate_IgnoresFilesThatWereNeverCompiled()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "Src", "Excluded.cs"), "class Excluded { }");
        var graph = await CreateGraphAsync(ignored: ["Src/Excluded.cs"]);

        var report = await new GraphFreshnessEvaluator().EvaluateAsync(graph);

        Assert.Equal(FreshnessStatus.Fresh, report.Status);
        Assert.Contains(report.Reasons, reason => reason.Contains("컴파일하지 않는", StringComparison.Ordinal));
    }

    /// <summary>분석 루트가 없으면 fresh라고 단정하지 않는다.</summary>
    [Fact]
    public async Task Evaluate_ReturnsUnknown_WhenRootIsGone()
    {
        var graph = (await CreateGraphAsync()) with { RootDirectory = Path.Combine(root, "없는폴더") };

        var report = await new GraphFreshnessEvaluator().EvaluateAsync(graph);

        Assert.Equal(FreshnessStatus.Unknown, report.Status);
    }

    /* ---------------------------------------------------------------- 환경 점검 */

    [Fact]
    public void Diagnostics_ReportsTargetAndOutput()
    {
        var target = Path.Combine(root, "Fixture.sln");
        File.WriteAllText(target, string.Empty);

        var report = new EnvironmentDiagnostics().Run(new DiagnosticsOptions { TargetPath = target });

        Assert.Contains(report.Items, item => item.Code == "TARGET_FOUND");
        Assert.Contains(report.Items, item => item.Code == "OUTPUT_WRITABLE");
        Assert.DoesNotContain(report.Items, item => item.Code == "TARGET_NOT_FOUND");
    }

    [Fact]
    public void Diagnostics_ReportsMissingTarget()
    {
        var report = new EnvironmentDiagnostics().Run(new DiagnosticsOptions
        {
            TargetPath = Path.Combine(root, "없는파일.sln")
        });

        Assert.Contains(report.Items, item => item.Code == "TARGET_NOT_FOUND");
        Assert.False(report.CanAnalyze);
    }

    [Fact]
    public void Diagnostics_RejectsUnsupportedTargetFormat()
    {
        var report = new EnvironmentDiagnostics().Run(new DiagnosticsOptions
        {
            TargetPath = Path.Combine(root, "Fixture.slnx")
        });

        Assert.Contains(report.Items, item => item.Code == "UNSUPPORTED_TARGET_FORMAT");
    }

    /* ---------------------------------------------------------------- 공유 보고서 */

    [Fact]
    public async Task ShareReport_ExcludesAbsolutePathsAndSource()
    {
        var graph = await CreateGraphAsync();
        var index = new GraphIndex(graph);
        var document = await new ContextBuilder(new GraphFreshnessEvaluator()).BuildAsync(
            index,
            new SourceReader(graph, SourceAccessPolicy.ForGraph(graph)),
            new ContextRequest(new ContextSelection(SelectionKind.Node, graph.Nodes[0].Id), ContextIntent.Share, ContextBudget.Short)
            {
                IncludeSource = false
            });

        var report = new ShareReportBuilder().Build(
            index,
            document,
            new ShareReportRequest(new ContextSelection(SelectionKind.Node, graph.Nodes[0].Id)));

        Assert.DoesNotContain(root, report.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(report.Title));
    }

    /* ---------------------------------------------------------------- 기준 보관 */

    [Fact]
    public async Task SnapshotStore_SavesListsLoadsAndDeletes()
    {
        var graph = await CreateGraphAsync();
        var store = new SnapshotStore();

        var entry = await store.SaveAsync(graph, "변경 전");
        var listed = await store.ListAsync(graph);
        var loaded = await store.LoadAsync(graph, entry.SnapshotId);

        Assert.Equal("변경 전", entry.Label);
        Assert.Single(listed);
        Assert.True(loaded.Ok);
        Assert.Equal(graph.SnapshotId, loaded.Graph?.SnapshotId);

        Assert.True(store.Delete(graph, entry.SnapshotId));
        Assert.Empty(await store.ListAsync(graph));
        Assert.False(store.Delete(graph, entry.SnapshotId));
    }

    [Fact]
    public async Task SnapshotStore_ThrowsWhenRootIsUnknown()
    {
        var graph = (await CreateGraphAsync()) with { RootDirectory = Path.Combine(root, "없는폴더") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SnapshotStore().SaveAsync(graph, "기준"));
    }

    private async Task<GraphDocument> CreateGraphAsync(IReadOnlyList<string>? ignored = null)
    {
        var relative = "Src/Service.cs";
        var hash = await SnapshotHasher.ComputeFileAsync(Path.Combine(root, "Src", "Service.cs"));
        var configHash = await SnapshotHasher.ComputeFileAsync(Path.Combine(root, "tracelens.config.json"));
        var node = new GraphNode(
            "Fixture.Service.Run()", NodeKind.Service, "Service.Run", "Fixture", "Fixture.Service", relative, 1, 1);
        return new GraphDocument(
            GraphDocument.CurrentSchemaVersion,
            Path.Combine(root, "Fixture.sln"),
            DateTimeOffset.Now,
            [new EntryPoint(node.Id, "GET", "/run")],
            [],
            [node],
            [],
            new Dictionary<string, string> { [relative] = hash })
        {
            RootDirectory = root,
            SnapshotId = "ctx-snapshot",
            InputFingerprint = "sha256:ctx",
            AnalyzerVersion = "0.8.0",
            RulesVersion = "1",
            Manifest = new AnalysisManifest(
                [new ManifestInput("tracelens.config.json", configHash, ManifestInputKind.Config)],
                ["Src"],
                AnalysisManifestBuilder.ExcludedDirectories,
                [],
                [])
            {
                IgnoredSources = ignored ?? []
            }
        };
    }
}

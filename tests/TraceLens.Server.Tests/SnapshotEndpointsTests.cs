using Microsoft.AspNetCore.Http;
using TraceLens.Analyzer.Models;
using TraceLens.Context;
using TraceLens.Server.Endpoints;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// 기준 저장 → 목록 → 비교 → 삭제 흐름과, 가져온 분석 결과를 기준으로 삼지 않는 규칙을 확인한다.
/// </summary>
public sealed class SnapshotEndpointsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tracelens-snap-" + Guid.NewGuid().ToString("N"));

    public SnapshotEndpointsTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_ThenListAsync_ReturnsTheBaseline()
    {
        var store = CreateStore();
        var snapshots = new SnapshotStore();

        var saved = await SnapshotEndpoints.SaveAsync(
            new SaveSnapshotRequest("변경 전"), store, snapshots, CancellationToken.None);

        var savedValue = Assert.IsType<SaveSnapshotResponse>(Assert.IsAssignableFrom<IValueHttpResult>(saved).Value);
        Assert.Equal("test-snapshot", savedValue.Entry.SnapshotId);
        Assert.Equal(1, savedValue.TotalCount);
        Assert.Empty(savedValue.PruneCandidates);

        var listed = await SnapshotEndpoints.ListAsync(store, snapshots, CancellationToken.None);
        var listedValue = Assert.IsType<ListSnapshotsResponse>(Assert.IsAssignableFrom<IValueHttpResult>(listed).Value);
        Assert.Single(listedValue.Entries);
        Assert.Equal(SnapshotStore.SoftLimit, listedValue.SoftLimit);
    }

    [Fact]
    public async Task CompareAsync_ReportsNoDifferenceForTheSameGraph()
    {
        var store = CreateStore();
        var snapshots = new SnapshotStore();
        await SnapshotEndpoints.SaveAsync(new SaveSnapshotRequest("기준"), store, snapshots, CancellationToken.None);

        var result = await SnapshotEndpoints.CompareAsync(
            new CompareSnapshotsRequest("test-snapshot"), store, snapshots, new SnapshotComparer(), CancellationToken.None);

        var diff = Assert.IsType<SnapshotDiff>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Empty(diff.Endpoints);
        Assert.Empty(diff.Methods);
    }

    [Fact]
    public async Task CompareAsync_ReturnsNotFound_ForUnknownBaseline()
    {
        var result = await SnapshotEndpoints.CompareAsync(
            new CompareSnapshotsRequest("없는-기준"),
            CreateStore(),
            new SnapshotStore(),
            new SnapshotComparer(),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTheBaseline()
    {
        var store = CreateStore();
        var snapshots = new SnapshotStore();
        await SnapshotEndpoints.SaveAsync(new SaveSnapshotRequest("기준"), store, snapshots, CancellationToken.None);

        var deleted = SnapshotEndpoints.Delete("test-snapshot", store, snapshots);
        var listed = await SnapshotEndpoints.ListAsync(store, snapshots, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleted).StatusCode);
        Assert.Empty(Assert.IsType<ListSnapshotsResponse>(
            Assert.IsAssignableFrom<IValueHttpResult>(listed).Value).Entries);
    }

    [Fact]
    public void Delete_ReturnsNotFound_ForUnknownId()
    {
        var result = SnapshotEndpoints.Delete("없는-기준", CreateStore(), new SnapshotStore());

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    /// <summary>가져온 분석 결과는 이 기계에서 만든 것이 아니므로 기준이 될 수 없다.</summary>
    [Fact]
    public async Task SaveAsync_RejectsImportedGraph()
    {
        var store = new GraphStore();
        store.Set(CreateGraph(), GraphOrigin.OfflineSnapshot, "imported.json");

        var result = await SnapshotEndpoints.SaveAsync(
            new SaveSnapshotRequest("기준"), store, new SnapshotStore(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var error = Assert.IsType<ApiError>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Equal("OFFLINE_SNAPSHOT", error.Code);
    }

    [Fact]
    public async Task SaveAsync_ReturnsNotAnalyzed_WithoutGraph()
    {
        var result = await SnapshotEndpoints.SaveAsync(
            new SaveSnapshotRequest("기준"), new GraphStore(), new SnapshotStore(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    private GraphStore CreateStore()
    {
        var store = new GraphStore();
        store.Set(CreateGraph());
        return store;
    }

    private GraphDocument CreateGraph() => new(
        GraphDocument.CurrentSchemaVersion,
        Path.Combine(root, "Fixture.sln"),
        DateTimeOffset.Parse("2026-09-17T00:00:00+09:00"),
        [new EntryPoint("Fixture.Controller.Entry()", "GET", "/fixture")],
        [],
        [new GraphNode("Fixture.Controller.Entry()", NodeKind.Controller, "Controller.Entry", "Fixture", "Fixture.Controller", "Controller.cs", 1, 3)],
        [],
        new Dictionary<string, string>())
    {
        RootDirectory = root,
        SnapshotId = "test-snapshot",
        InputFingerprint = "sha256:fixture",
        AnalyzerVersion = "0.8.0",
        RulesVersion = "1"
    };
}

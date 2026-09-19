using TraceLens.Analyzer.Models;
using TraceLens.Context;

namespace TraceLens.Cli.Tests;

/// <summary>
/// 서버 없이 명령만 돌리기 위한 최소 분석 결과다.
/// 실제 `.tracelens/graph.json`을 임시 폴더에 써서 `GraphLocator`가 찾는 경로를 그대로 지나가게 한다.
/// </summary>
public sealed class GraphFixture : IAsyncDisposable
{
    private readonly string directory;

    private GraphFixture(string directory, string targetPath, GraphDocument graph)
    {
        this.directory = directory;
        TargetPath = targetPath;
        Graph = graph;
    }

    /// <summary>명령에 넘기는 분석 대상 경로(.sln).</summary>
    public string TargetPath { get; }

    public GraphDocument Graph { get; }

    public static async Task<GraphFixture> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "OrdersController.cs"),
            "public class OrdersController\n{\n    public void Create() { }\n}\n");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "OrderService.cs"),
            "public class OrderService\n{\n    public void Save() { }\n}\n");

        var targetPath = Path.Combine(directory, "Fixture.sln");
        await File.WriteAllTextAsync(targetPath, string.Empty);

        var controller = new GraphNode(
            "Fixture.OrdersController.Create()",
            NodeKind.Controller,
            "OrdersController.Create",
            "Fixture",
            "Fixture.OrdersController",
            "OrdersController.cs",
            1,
            4);
        var service = new GraphNode(
            "Fixture.OrderService.Save()",
            NodeKind.Service,
            "OrderService.Save",
            "Fixture",
            "Fixture.OrderService",
            "OrderService.cs",
            1,
            4);

        var graph = new GraphDocument(
            GraphDocument.CurrentSchemaVersion,
            targetPath,
            DateTimeOffset.Parse("2026-09-18T21:00:00+09:00"),
            [new EntryPoint(controller.Id, "POST", "/orders")],
            [],
            [controller, service],
            [new GraphEdge(controller.Id, service.Id)],
            new Dictionary<string, string>())
        {
            RootDirectory = directory,
            SnapshotId = "snap-current",
            AnalyzerVersion = "0.9.0",
            RulesVersion = "1"
        };

        var graphPath = Path.Combine(directory, ".tracelens", "graph.json");
        Directory.CreateDirectory(Path.GetDirectoryName(graphPath)!);
        await GraphJsonStore.WriteAtomicallyAsync(graphPath, graph);
        return new GraphFixture(directory, targetPath, graph);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // 임시 폴더가 잠겨 있어도 테스트 결과를 바꾸지 않는다.
        }

        return ValueTask.CompletedTask;
    }
}

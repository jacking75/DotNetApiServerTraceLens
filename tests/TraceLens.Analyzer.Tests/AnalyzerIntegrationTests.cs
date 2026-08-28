using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

public sealed class AnalyzerIntegrationTests
{
    [Fact]
    public async Task AnalyzeAsync_BuildsExpectedControllerServiceRepositoryGraph()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

        Assert.Equal(3, result.Graph.EntryPoints.Count);
        Assert.True(
            result.Graph.Nodes.Count == 15,
            $"예상 노드 수는 15개지만 {result.Graph.Nodes.Count}개다.{Environment.NewLine}{string.Join(Environment.NewLine, logs)}");
        Assert.Equal(11, result.Graph.Edges.Count);
        Assert.Equal(3, result.Graph.Nodes.Count(node => node.Kind == NodeKind.Controller));
        Assert.Contains(result.Graph.Nodes, node => node.Kind == NodeKind.Service);
        Assert.Contains(result.Graph.Nodes, node => node.Kind == NodeKind.Repository);
        Assert.All(result.Graph.FileHashes.Values, hash => Assert.StartsWith("sha256:", hash));
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public async Task AnalyzeAsync_ResolvesInterfaceCallsByRegistrationAndSingleImplementation()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

        var callee = (string nodeId) => result.Graph.Edges.Any(edge =>
            edge.From == "FixtureApp.Controllers.OrdersController.Create(int)" && edge.To == nodeId);

        // Program.cs가 아닌 파일에서 AddSingleton으로 등록한 구현체로 이어져야 한다.
        Assert.True(
            callee("FixtureApp.Services.NotificationService.NotifyAsync(int)"),
            string.Join(Environment.NewLine, logs));
        Assert.False(callee("FixtureApp.Services.NullNotificationService.NotifyAsync(int)"));

        // DI 등록이 없어도 구현체가 하나뿐이면 그 구현체로 이어져야 한다.
        Assert.True(
            callee("FixtureApp.Services.AuditService.RecordAsync(int)"),
            string.Join(Environment.NewLine, logs));
    }

    [Fact]
    public async Task AnalyzeAsync_LinksObjectCreationAndScansConstructorAndPropertyBodies()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());
        var hasEdge = (string from, string to) => result.Graph.Edges.Any(edge => edge.From == from && edge.To == to);
        var draftConstructorId = "FixtureApp.Models.OrderDraft..ctor()";

        // new로 만든 DTO는 기본 생성자만 있어도 노드가 생기고, 그 DTO 파일을 가리켜야 한다.
        var draftConstructor = Assert.Single(result.Graph.Nodes, node => node.Id == draftConstructorId);
        Assert.Equal("new OrderDraft", draftConstructor.DisplayName);
        Assert.Equal("FixtureApp/Models/OrderDraft.cs", draftConstructor.File);
        Assert.Contains(draftConstructorId, Assert.Single(
            result.Graph.Classes,
            item => item.Id == "FixtureApp.Models.OrderDraft").MethodIds);
        Assert.True(
            hasEdge("FixtureApp.Services.OrderService.CreateAsync(int)", draftConstructorId),
            string.Join(Environment.NewLine, logs));

        // 생성자 본문과 프로퍼티 접근자 본문 안의 호출도 잡혀야 한다.
        Assert.True(hasEdge(
            "FixtureApp.Repositories.OrderRepository..ctor()",
            "FixtureApp.Repositories.OrderRepository.CreateDraft()"));
        Assert.True(hasEdge(
            "FixtureApp.Repositories.OrderRepository.get_PendingCount()",
            "FixtureApp.Repositories.OrderRepository.CountPending()"));

        // 대상 타입을 적지 않는 new()도 같은 생성자로 이어져야 한다.
        Assert.True(hasEdge("FixtureApp.Repositories.OrderRepository.CreateDraft()", draftConstructorId));
    }

    [Fact]
    public async Task AnalyzeAsync_WithObjectCreationDisabled_KeepsOnlyMethodCalls()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);
        var configPath = Path.Combine(Path.GetDirectoryName(GetFixtureSolutionPath())!, "tracelens.config.json");
        await File.WriteAllTextAsync(configPath, """{ "includeObjectCreation": false }""");

        try
        {
            var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

            // new로만 등장하던 DTO 생성자는 사라지고, 직접 선언한 생성자 노드는 그대로 남는다.
            Assert.DoesNotContain(result.Graph.Nodes, node => node.Id == "FixtureApp.Models.OrderDraft..ctor()");
            Assert.DoesNotContain(result.Graph.Edges, edge => edge.To.EndsWith("OrderDraft..ctor()", StringComparison.Ordinal));
            Assert.Contains(result.Graph.Nodes, node => node.Id == "FixtureApp.Repositories.OrderRepository..ctor()");
            Assert.Contains(
                result.Graph.Edges,
                edge => edge.From == "FixtureApp.Services.OrderService.CreateAsync(int)" &&
                        edge.To == "FixtureApp.Repositories.OrderRepository.SaveAsync(int)");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_ListsEveryClassIncludingOnesNoEntryPointReaches()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

        // 인터페이스는 제외하므로 컨트롤러 3개 + 서비스 4개 + 리포지토리 + 잡 + DI 등록 확장 클래스 + DTO다.
        Assert.Equal(11, result.Graph.Classes.Count);
        var job = Assert.Single(result.Graph.Classes, item => item.Name == "CleanupJob");
        Assert.Equal("FixtureApp.Jobs", job.Namespace);
        Assert.Equal("FixtureApp/Jobs/CleanupJob.cs", job.File);
        Assert.Equal(["FixtureApp.Jobs.CleanupJob.RunAsync(int)"], job.MethodIds);
        Assert.DoesNotContain(result.Graph.Classes, item => item.Name.StartsWith('I') && item.Name.EndsWith("Service"));

        // 진입점에서 도달하지 못하는 클래스도 호출 엣지를 가진다.
        Assert.Contains(
            result.Graph.Edges,
            edge => edge.From == "FixtureApp.Jobs.CleanupJob.RunAsync(int)" &&
                    edge.To == "FixtureApp.Repositories.OrderRepository.SaveAsync(int)");

        // 모든 노드는 목록에 있는 클래스에 속한다.
        var classIds = result.Graph.Classes.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(result.Graph.Nodes, node => Assert.Contains(node.ClassId, classIds));
    }

    [Fact]
    public async Task AnalyzeAsync_WithEntryPointScope_KeepsGraphReachableOnlyButStillListsAllClasses()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);
        var configPath = Path.Combine(Path.GetDirectoryName(GetFixtureSolutionPath())!, "tracelens.config.json");
        await File.WriteAllTextAsync(configPath, """{ "analysisScope": "entryPoints" }""");

        try
        {
            var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

            Assert.DoesNotContain(result.Graph.Nodes, node => node.DisplayName == "CleanupJob.RunAsync");
            Assert.Equal(8, result.Graph.Nodes.Count);
            Assert.Equal(11, result.Graph.Classes.Count);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    private static ServiceProvider CreateProvider(ConcurrentQueue<string> logs)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(new CollectingLoggerProvider(logs)));
        services.AddTraceLensAnalyzer();
        return services.BuildServiceProvider();
    }

    private static string GetFixtureSolutionPath() =>
        Path.Combine(GetRepositoryRoot(), "tests", "TraceLens.Analyzer.Tests", "Fixtures", "FixtureApp.sln");

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));

    private sealed class CollectingLoggerProvider(ConcurrentQueue<string> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, logs);
        public void Dispose() { }
    }

    private sealed class CollectingLogger(string categoryName, ConcurrentQueue<string> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            logs.Enqueue($"[{logLevel}] {categoryName}: {formatter(state, exception)}");
    }
}

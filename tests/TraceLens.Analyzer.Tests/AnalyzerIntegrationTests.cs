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
        var services = new ServiceCollection();
        var logs = new ConcurrentQueue<string>();
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(new CollectingLoggerProvider(logs)));
        services.AddTraceLensAnalyzer();
        await using var provider = services.BuildServiceProvider();
        var analyzer = provider.GetRequiredService<ITraceLensAnalyzer>();
        var fixturePath = Path.Combine(GetRepositoryRoot(), "tests", "TraceLens.Analyzer.Tests", "Fixtures", "FixtureApp.sln");

        var result = await analyzer.AnalyzeAsync(fixturePath);

        Assert.Equal(3, result.Graph.EntryPoints.Count);
        Assert.True(
            result.Graph.Nodes.Count == 5,
            $"예상 노드 수는 5개지만 {result.Graph.Nodes.Count}개다.{Environment.NewLine}{string.Join(Environment.NewLine, logs)}");
        Assert.Equal(4, result.Graph.Edges.Count);
        Assert.Equal(3, result.Graph.Nodes.Count(node => node.Kind == NodeKind.Controller));
        Assert.Contains(result.Graph.Nodes, node => node.Kind == NodeKind.Service);
        Assert.Contains(result.Graph.Nodes, node => node.Kind == NodeKind.Repository);
        Assert.All(result.Graph.FileHashes.Values, hash => Assert.StartsWith("sha256:", hash));
        Assert.True(File.Exists(result.OutputPath));
    }

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

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TraceLens.Analyzer.Models;
using TraceLens.Server.Endpoints;
using Xunit;

namespace TraceLens.Server.Tests;

public sealed class AskEndpointsTests
{
    [Fact]
    public async Task HandleAsync_StreamsClaudeTextAndUsesMcpConfig()
    {
        var graph = CreateGraph();
        var store = new GraphStore();
        store.Set(graph);
        var runner = new StubProcessRunner
        {
            StreamingOutput =
            [
                new ProcessOutputLine(
                    "{\"type\":\"stream_event\",\"event\":{\"delta\":{\"type\":\"text_delta\",\"text\":\"응답\"}}}",
                    false)
            ]
        };
        var context = CreateHttpContext();

        await AskEndpoints.HandleAsync(
            new AskRequest(graph.Nodes[0].Id, "무슨 코드인가?", "mcp"),
            context,
            store,
            runner,
            new StubMcpConfigWriter("C:\\Fixture\\.tracelens\\mcp.json"),
            CancellationToken.None);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.Contains("응답", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"done\"", body, StringComparison.Ordinal);
        Assert.NotNull(runner.StreamingRequest);
        Assert.Equal("claude", runner.StreamingRequest.FileName);
        Assert.Contains("--strict-mcp-config", runner.StreamingRequest.Arguments);
        Assert.Contains("C:\\Fixture\\.tracelens\\mcp.json", runner.StreamingRequest.Arguments);
    }

    [Fact]
    public async Task HandleAsync_ReturnsEditorFallback_WhenCodeCommandFails()
    {
        var graph = CreateGraph();
        var store = new GraphStore();
        store.Set(graph);
        var runner = new StubProcessRunner
        {
            Result = new ProcessResult(1, string.Empty, "code 명령 실패")
        };
        var context = CreateHttpContext();

        await AskEndpoints.HandleAsync(
            new AskRequest(graph.Nodes[0].Id, null, "editor"),
            context,
            store,
            runner,
            new StubMcpConfigWriter(string.Empty),
            CancellationToken.None);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("\"opened\":false", body, StringComparison.Ordinal);
        Assert.Contains("vscode://file/", body, StringComparison.Ordinal);
        Assert.NotNull(runner.Request);
        Assert.Equal(["--goto", "C:\\Fixture\\Controller.cs:3"], runner.Request.Arguments);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() }
        };
    }

    private static GraphDocument CreateGraph()
    {
        var node = new GraphNode(
            "Fixture.Controller.Entry()",
            NodeKind.Controller,
            "Controller.Entry",
            "Fixture",
            "Controller.cs",
            3,
            8);
        return new GraphDocument(
            1,
            "C:\\Fixture\\Fixture.sln",
            DateTimeOffset.Parse("2026-08-11T00:00:00+09:00"),
            [new EntryPoint(node.Id, "GET", "/fixture")],
            [node],
            [],
            new Dictionary<string, string>());
    }

    private sealed class StubMcpConfigWriter(string path) : IMcpConfigWriter
    {
        public Task<string> WriteAsync(GraphDocument graph, CancellationToken cancellationToken = default) =>
            Task.FromResult(path);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        public IReadOnlyList<ProcessOutputLine> StreamingOutput { get; init; } = [];
        public ProcessResult Result { get; init; } = new(0, string.Empty, string.Empty);
        public ProcessRequest? StreamingRequest { get; private set; }
        public ProcessRequest? Request { get; private set; }

        public async IAsyncEnumerable<ProcessOutputLine> RunStreamingAsync(
            ProcessRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingRequest = request;
            foreach (var line in StreamingOutput)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return line;
                await Task.Yield();
            }
        }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(Result);
        }
    }
}

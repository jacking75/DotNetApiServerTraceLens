using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TraceLens.Analyzer.Models;
using TraceLens.Context;
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
            new AgentRunnerRegistry([new ClaudeCodeAgentRunner(runner), new CodexAgentRunner(runner)]),
            new StubMcpConfigWriter("C:\\Fixture\\.tracelens\\mcp.json"),
            new ContextBuilder(new GraphFreshnessEvaluator()),
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

    /// <summary>
    /// CLI가 없을 때 UI가 텍스트 접두사를 파싱하지 않아도 되도록 code 필드를 함께 보낸다.
    /// 이 필드가 빠지면 화면에 OS 예외 원문이 그대로 뜬다.
    /// </summary>
    [Fact]
    public async Task HandleAsync_SendsAgentNotFoundCode_WhenCliIsMissing()
    {
        var graph = CreateGraph();
        var store = new GraphStore();
        store.Set(graph);
        var runner = new StubProcessRunner { ThrowExecutableMissing = true };
        var context = CreateHttpContext();

        await AskEndpoints.HandleAsync(
            new AskRequest(graph.Nodes[0].Id, "무슨 코드인가?", "mcp"),
            context,
            store,
            runner,
            new AgentRunnerRegistry([new ClaudeCodeAgentRunner(runner), new CodexAgentRunner(runner)]),
            new StubMcpConfigWriter(@"C:\Fixture\.tracelens\mcp.json"),
            new ContextBuilder(new GraphFreshnessEvaluator()),
            CancellationToken.None);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("\"code\":\"AGENT_NOT_FOUND\"", body, StringComparison.Ordinal);
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
            new AgentRunnerRegistry([new ClaudeCodeAgentRunner(runner), new CodexAgentRunner(runner)]),
            new StubMcpConfigWriter(string.Empty),
            new ContextBuilder(new GraphFreshnessEvaluator()),
            CancellationToken.None);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("\"opened\":false", body, StringComparison.Ordinal);
        Assert.Contains("vscode://file/", body, StringComparison.Ordinal);
        Assert.NotNull(runner.Request);
        Assert.Equal(["--goto", "C:\\Fixture\\Controller.cs:3"], runner.Request.Arguments);
    }

    [Fact]
    public async Task HandleAsync_HonoursIncludeSourceAndExcludeFiles()
    {
        // 미리보기(/api/context)와 실제로 에이전트에 넘기는 문맥이 같아야 한다.
        var root = Path.Combine(Path.GetTempPath(), "tracelens-ask-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string marker = "MARKER_SOURCE_LINE";
            await File.WriteAllTextAsync(
                Path.Combine(root, "Controller.cs"),
                $"class Controller\n{{\n    void Entry() {{ /* {marker} */ }}\n}}\n");
            var graph = CreateGraph() with { SolutionPath = Path.Combine(root, "Fixture.sln") };
            var store = new GraphStore();
            store.Set(graph);

            var withSource = await RunAskAsync(store, graph, includeSource: true, excludeFiles: null);
            Assert.Contains(marker, withSource, StringComparison.Ordinal);

            var withoutSource = await RunAskAsync(store, graph, includeSource: false, excludeFiles: null);
            Assert.DoesNotContain(marker, withoutSource, StringComparison.Ordinal);

            var excluded = await RunAskAsync(store, graph, includeSource: true, excludeFiles: ["Controller.cs"]);
            Assert.DoesNotContain(marker, excluded, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>에이전트에 넘긴 프롬프트(인자)를 그대로 돌려준다.</summary>
    private static async Task<string> RunAskAsync(
        GraphStore store, GraphDocument graph, bool includeSource, IReadOnlyList<string>? excludeFiles)
    {
        var runner = new StubProcessRunner();
        var context = CreateHttpContext();
        await AskEndpoints.HandleAsync(
            new AskRequest(graph.Nodes[0].Id, "무슨 코드인가?", "mcp")
            {
                IncludeSource = includeSource,
                ExcludeFiles = excludeFiles
            },
            context,
            store,
            runner,
            new AgentRunnerRegistry([new ClaudeCodeAgentRunner(runner), new CodexAgentRunner(runner)]),
            new StubMcpConfigWriter("C:\\Fixture\\.tracelens\\mcp.json"),
            new ContextBuilder(new GraphFreshnessEvaluator()),
            CancellationToken.None);
        Assert.NotNull(runner.StreamingRequest);
        return string.Join("\n", runner.StreamingRequest!.Arguments);
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
            "Fixture.Controller",
            "Controller.cs",
            3,
            8);
        return new GraphDocument(
            1,
            "C:\\Fixture\\Fixture.sln",
            DateTimeOffset.Parse("2026-08-11T00:00:00+09:00"),
            [new EntryPoint(node.Id, "GET", "/fixture")],
            [],
            [node],
            [],
            new Dictionary<string, string>());
    }

    private sealed class StubMcpConfigWriter(string path) : IMcpConfigWriter
    {
        public Task<string> WriteAsync(GraphDocument graph, CancellationToken cancellationToken = default) =>
            Task.FromResult(path);

        public McpSetupPreview BuildPreview(GraphDocument graph, string? offlineGraphPath) =>
            new(path, "dotnet", [path], [], null);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        public IReadOnlyList<ProcessOutputLine> StreamingOutput { get; init; } = [];
        public ProcessResult Result { get; init; } = new(0, string.Empty, string.Empty);
        public ProcessRequest? StreamingRequest { get; private set; }
        public ProcessRequest? Request { get; private set; }

        /// <summary>실행 파일 자체가 없는 상황(CLI 미설치)을 흉내 낸다.</summary>
        public bool ThrowExecutableMissing { get; init; }

        public async IAsyncEnumerable<ProcessOutputLine> RunStreamingAsync(
            ProcessRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingRequest = request;
            if (ThrowExecutableMissing)
            {
                throw new System.ComponentModel.Win32Exception(2, "지정된 파일을 찾을 수 없습니다.");
            }

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

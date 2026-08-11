using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using TraceLens.Analyzer.Models;

namespace TraceLens.Server.Endpoints;

public static class AskEndpoints
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private const string AllowedMcpTools =
        "mcp__tracelens__get_node,mcp__tracelens__get_source,mcp__tracelens__get_callers," +
        "mcp__tracelens__get_callees,mcp__tracelens__get_entrypoints,mcp__tracelens__search_symbol";

    public static RouteGroupBuilder MapAskEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/ask", HandleAsync);
        return group;
    }

    internal static async Task HandleAsync(
        AskRequest request,
        HttpContext context,
        IGraphStore store,
        IProcessRunner processRunner,
        IMcpConfigWriter mcpConfigWriter,
        CancellationToken cancellationToken)
    {
        var graph = store.Current;
        if (graph is null)
        {
            await ExecuteResultAsync(Results.NotFound(new { error = "먼저 솔루션을 분석해야 한다." }), context);
            return;
        }

        var node = graph.Nodes.FirstOrDefault(candidate => candidate.Id == request.NodeId);
        if (node is null)
        {
            await ExecuteResultAsync(Results.BadRequest(new { error = "선택한 노드를 찾을 수 없다." }), context);
            return;
        }

        if (string.Equals(request.Mode, "editor", StringComparison.OrdinalIgnoreCase))
        {
            await OpenEditorAsync(node, graph, context, processRunner, cancellationToken);
            return;
        }

        if (!string.Equals(request.Mode, "mcp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request.Question))
        {
            await ExecuteResultAsync(
                Results.BadRequest(new { error = "mode는 mcp 또는 editor여야 하며 mcp 질문은 비어 있을 수 없다." }),
                context);
            return;
        }

        await StreamClaudeResponseAsync(
            request.Question.Trim(),
            node,
            graph,
            context,
            processRunner,
            mcpConfigWriter,
            cancellationToken);
    }

    private static async Task StreamClaudeResponseAsync(
        string question,
        GraphNode node,
        GraphDocument graph,
        HttpContext context,
        IProcessRunner processRunner,
        IMcpConfigWriter mcpConfigWriter,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        try
        {
            var configPath = await mcpConfigWriter.WriteAsync(graph, cancellationToken);
            var solutionDirectory = Path.GetDirectoryName(graph.SolutionPath)!;
            var prompt = FormatPrompt(node, question);
            var processRequest = new ProcessRequest(
                "claude",
                [
                    "-p", prompt,
                    "--mcp-config", configPath,
                    "--strict-mcp-config",
                    "--allowedTools", AllowedMcpTools,
                    "--permission-mode", "dontAsk",
                    "--output-format", "stream-json",
                    "--verbose",
                    "--include-partial-messages",
                    "--no-session-persistence"
                ],
                solutionDirectory);

            await foreach (var output in processRunner.RunStreamingAsync(processRequest, cancellationToken))
            {
                if (output.IsStandardError)
                {
                    await WriteEventAsync(context.Response, new AskEvent("log", output.Text), cancellationToken);
                    continue;
                }

                var text = ClaudeStreamParser.TryGetText(output.Text);
                if (!string.IsNullOrEmpty(text))
                {
                    await WriteEventAsync(context.Response, new AskEvent("delta", text), cancellationToken);
                }
            }

            await WriteEventAsync(context.Response, new AskEvent("done", null), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser closed the request; there is no client left to notify.
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ProcessExecutionException or
            UnauthorizedAccessException or Win32Exception)
        {
            await WriteEventAsync(context.Response, new AskEvent("error", exception.Message), CancellationToken.None);
        }
    }

    private static async Task OpenEditorAsync(
        GraphNode node,
        GraphDocument graph,
        HttpContext context,
        IProcessRunner processRunner,
        CancellationToken cancellationToken)
    {
        var solutionDirectory = Path.GetDirectoryName(graph.SolutionPath)!;
        var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, node.File));
        var relativePath = Path.GetRelativePath(solutionDirectory, fullPath);
        if (relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            await ExecuteResultAsync(Results.BadRequest(new { error = "솔루션 외부 파일은 열 수 없다." }), context);
            return;
        }

        var fallbackUri = CreateVsCodeUri(fullPath, node.StartLine);
        try
        {
            var result = await processRunner.RunAsync(
                new ProcessRequest("code", ["--goto", $"{fullPath}:{node.StartLine}"], solutionDirectory),
                cancellationToken);
            var opened = result.ExitCode == 0;
            var error = opened ? null : string.IsNullOrWhiteSpace(result.StandardError)
                ? $"code 프로세스가 종료 코드 {result.ExitCode}(으)로 끝났다."
                : result.StandardError.Trim();
            await ExecuteResultAsync(Results.Ok(new EditorResponse(opened, fallbackUri, error)), context);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            await ExecuteResultAsync(
                Results.Ok(new EditorResponse(false, fallbackUri, exception.Message)),
                context);
        }
    }

    private static string FormatPrompt(GraphNode node, string question) => $"""
        TraceLens에서 다음 노드를 선택했다.
        노드 ID: {node.Id}
        파일: {node.File}
        라인: {node.StartLine}-{node.EndLine}
        심볼: {node.DisplayName}

        TraceLens MCP 도구를 사용해 실제 소스와 호출 관계를 확인한 뒤 질문에 답해야 한다.
        질문: {question}
        """;

    private static string CreateVsCodeUri(string fullPath, int line)
    {
        var normalizedPath = fullPath.Replace(Path.DirectorySeparatorChar, '/');
        var escapedPath = string.Join('/', normalizedPath.Split('/').Select(Uri.EscapeDataString));
        return $"vscode://file/{escapedPath}:{line}";
    }

    private static async Task WriteEventAsync(
        HttpResponse response,
        AskEvent askEvent,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(askEvent, EventJsonOptions);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static Task ExecuteResultAsync(IResult result, HttpContext context) => result.ExecuteAsync(context);
}

public sealed record AskRequest(string NodeId, string? Question, string Mode);
public sealed record AskEvent(string Type, string? Text);
public sealed record EditorResponse(bool Opened, string FallbackUri, string? Error);

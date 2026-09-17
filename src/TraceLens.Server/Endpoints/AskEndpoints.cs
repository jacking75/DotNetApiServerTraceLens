using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using TraceLens.Analyzer.Models;
using TraceLens.Context;

namespace TraceLens.Server.Endpoints;

public static class AskEndpoints
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 인앱 실행에서 허용하는 MCP 도구 목록이다.
    /// 새 문맥 도구를 MCP에 추가하면 여기에도 넣어야 실제로 호출할 수 있다.
    /// </summary>
    public static IReadOnlyList<string> AllowedMcpTools { get; } =
    [
        "mcp__tracelens__get_node",
        "mcp__tracelens__get_source",
        "mcp__tracelens__get_callers",
        "mcp__tracelens__get_callees",
        "mcp__tracelens__get_entrypoints",
        "mcp__tracelens__search_symbol",
        "mcp__tracelens__get_analysis_status",
        "mcp__tracelens__get_context",
        "mcp__tracelens__get_entrypoint_flow",
        "mcp__tracelens__get_findings",
        "mcp__tracelens__get_impact",
        "mcp__tracelens__compare_snapshots"
    ];

    public static RouteGroupBuilder MapAskEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/ask", HandleAsync);
        group.MapDelete("/ask/{runId}", CancelRun);
        group.MapGet("/mcp-setup", GetSetup);
        return group;
    }

    /// <summary>대상 프로젝트와 설치된 호스트에 맞춘 MCP 설정 미리보기. 기존 설정을 덮어쓰지 않는다.</summary>
    internal static IResult GetSetup(IGraphStore store, IMcpConfigWriter writer)
    {
        var graph = store.Current;
        if (graph is null)
        {
            return ApiResults.NotAnalyzed();
        }

        try
        {
            return Results.Ok(writer.BuildPreview(graph, store.OfflineGraphPath));
        }
        catch (FileNotFoundException exception)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, "MCP_HOST_MISSING", exception.Message);
        }
    }

    internal static async Task HandleAsync(
        AskRequest request,
        HttpContext context,
        IGraphStore store,
        IProcessRunner processRunner,
        IAgentRunner agentRunner,
        IMcpConfigWriter mcpConfigWriter,
        ContextBuilder contextBuilder,
        CancellationToken cancellationToken)
    {
        var session = store.Session;
        var graph = store.Current;
        if (session is null || graph is null)
        {
            await ExecuteResultAsync(ApiResults.NotAnalyzed(), context);
            return;
        }

        if (string.Equals(request.Mode, "editor", StringComparison.OrdinalIgnoreCase))
        {
            var node = string.IsNullOrEmpty(request.NodeId) ? null : session.Index.FindNode(request.NodeId);
            if (node is null)
            {
                await ExecuteResultAsync(
                    ApiResults.Error(StatusCodes.Status400BadRequest, ApiResults.SelectionNotFound, "선택한 노드를 찾을 수 없다."),
                    context);
                return;
            }

            await OpenEditorAsync(node, graph, context, processRunner, cancellationToken);
            return;
        }

        if (!string.Equals(request.Mode, "mcp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request.Question))
        {
            await ExecuteResultAsync(
                ApiResults.Error(
                    StatusCodes.Status400BadRequest,
                    ApiResults.InvalidRequest,
                    "mode는 mcp 또는 editor여야 하며 mcp 질문은 비어 있을 수 없다."),
                context);
            return;
        }

        ContextDocument document;
        try
        {
            document = await contextBuilder.BuildAsync(
                session.Index,
                session.Source,
                new ContextRequest(
                    ResolveSelection(request),
                    ParseEnum(request.Intent, ContextIntent.Explain),
                    ParseEnum(request.Budget, ContextBudget.Balanced))
                {
                    Question = request.Question.Trim(),
                    ExpectedSnapshotId = request.ExpectedSnapshotId,
                    // 미리보기(/api/context)와 실제로 에이전트에 넘기는 문맥이 달라지면 안 된다.
                    IncludeSource = request.IncludeSource ?? true,
                    ExcludeFiles = request.ExcludeFiles ?? []
                },
                cancellationToken);
        }
        catch (ContextException exception)
        {
            await ExecuteResultAsync(ApiResults.FromContextException(exception), context);
            return;
        }

        await StreamAgentResponseAsync(
            document, graph, context, agentRunner, mcpConfigWriter, request.TimeoutSeconds, cancellationToken);
    }

    /// <summary>실행 중인 에이전트를 끊는다. SSE 연결을 닫는 것 말고도 명시적인 취소 수단이 필요하다.</summary>
    internal static IResult CancelRun(string runId) =>
        RunningAgents.TryRemove(runId, out var cancellation)
            ? Cancel(cancellation, runId)
            : ApiResults.Error(StatusCodes.Status404NotFound, "RUN_NOT_FOUND", $"실행 중인 요청을 찾을 수 없다: {runId}");

    private static IResult Cancel(CancellationTokenSource cancellation, string runId)
    {
        cancellation.Cancel();
        return Results.Ok(new { canceled = runId });
    }

    /// <summary>실행 중인 에이전트 호출. DELETE /api/ask/{runId}가 이 토큰을 취소한다.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>
        RunningAgents = new(StringComparer.Ordinal);

    public const string AgentTimeout = "AGENT_TIMEOUT";
    public const string AgentNotFound = "AGENT_NOT_FOUND";

    /// <summary>에이전트 실행이 이 시간을 넘으면 프로세스 트리를 끝낸다. 기본 10분.</summary>
    private const int DefaultTimeoutSeconds = 600;

    private static ContextSelection ResolveSelection(AskRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Kind) && !string.IsNullOrWhiteSpace(request.Id) &&
            Enum.TryParse<SelectionKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return new ContextSelection(kind, request.Id.Trim());
        }

        return new ContextSelection(SelectionKind.Node, request.NodeId ?? string.Empty);
    }

    private static async Task StreamAgentResponseAsync(
        ContextDocument document,
        GraphDocument graph,
        HttpContext context,
        IAgentRunner agentRunner,
        IMcpConfigWriter mcpConfigWriter,
        int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        var seconds = timeoutSeconds is > 0 and <= 3600 ? timeoutSeconds.Value : DefaultTimeoutSeconds;
        var runId = Guid.NewGuid().ToString("N")[..12];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        RunningAgents[runId] = linked;
        try
        {
            await WriteEventAsync(context.Response, new AskEvent("run", runId), cancellationToken);
            var configPath = await mcpConfigWriter.WriteAsync(graph, linked.Token);
            var request = new AgentRequest(
                FormatPrompt(document),
                graph.RootDirectory,
                configPath,
                AllowedMcpTools);

            await foreach (var output in agentRunner.RunAsync(request, linked.Token))
            {
                await WriteEventAsync(
                    context.Response,
                    new AskEvent(output.Kind == "log" ? "log" : "delta", output.Text),
                    cancellationToken);
            }

            await WriteEventAsync(context.Response, new AskEvent("done", null), cancellationToken);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            await WriteEventAsync(
                context.Response,
                new AskEvent("error", $"{AgentTimeout}: 에이전트가 {seconds}초 안에 끝나지 않아 프로세스를 끝냈다.", AgentTimeout),
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // 브라우저가 연결을 닫았거나 사용자가 취소했다. 알릴 대상이 없다.
        }
        catch (Win32Exception exception)
        {
            // 실행 파일 자체가 없을 때 OS 메시지를 그대로 보여 주면 무엇을 해야 할지 알 수 없다.
            await WriteEventAsync(
                context.Response,
                new AskEvent(
                    "error",
                    $"{AgentNotFound}: {agentRunner.DisplayName} CLI를 찾을 수 없다({exception.Message}). " +
                    "설치한 뒤 PATH에 있어야 한다. https://docs.claude.com/en/docs/claude-code 를 참고한다.",
                    AgentNotFound),
                CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ProcessExecutionException or
            UnauthorizedAccessException)
        {
            await WriteEventAsync(context.Response, new AskEvent("error", exception.Message), CancellationToken.None);
        }
        finally
        {
            RunningAgents.TryRemove(runId, out _);
        }
    }

    private static async Task OpenEditorAsync(
        GraphNode node,
        GraphDocument graph,
        HttpContext context,
        IProcessRunner processRunner,
        CancellationToken cancellationToken)
    {
        var solutionDirectory = graph.RootDirectory;
        var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, node.File));
        var relativePath = Path.GetRelativePath(solutionDirectory, fullPath);
        if (relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            await ExecuteResultAsync(
                ApiResults.Error(StatusCodes.Status400BadRequest, ApiResults.SourceDenied, "솔루션 외부 파일은 열 수 없다."),
                context);
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

    /// <summary>
    /// UI 미리보기·클립보드와 같은 문맥을 그대로 프롬프트에 쓴다.
    /// 서로 다른 곳에서 문맥을 따로 조립하지 않기 위해 <see cref="ContextDocument.Markdown"/>을 그대로 넣는다.
    /// </summary>
    private static string FormatPrompt(ContextDocument document) => $"""
        아래는 TraceLens가 만든 문맥이다. 정적 분석 결과이며 실행 이력이 아니다.

        {document.Markdown}

        TraceLens MCP 도구(get_context·get_node·get_source·get_callers·get_callees·get_findings)로
        실제 소스와 호출 관계를 확인한 뒤 답해야 한다. 확인하지 못한 것은 확인하지 못했다고 말해야 한다.
        분석 스냅샷은 {document.SnapshotId}다. 도구를 부를 때 expectedSnapshotId로 이 값을 넘기면 된다.
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

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    private static Task ExecuteResultAsync(IResult result, HttpContext context) => result.ExecuteAsync(context);
}

/// <param name="Kind">선택 종류(node·endpoint·class 등). 비우면 <paramref name="NodeId"/>를 노드로 본다.</param>
public sealed record AskRequest(string? NodeId, string? Question, string Mode)
{
    public string? Kind { get; init; }

    public string? Id { get; init; }

    public string? Intent { get; init; }

    public string? Budget { get; init; }

    public string? ExpectedSnapshotId { get; init; }

    /// <summary>소스 본문을 함께 넘길지. 미리보기의 같은 이름 옵션과 같은 뜻이다.</summary>
    public bool? IncludeSource { get; init; }

    /// <summary>문맥에서 뺄 파일의 상대 경로. 미리보기에서 체크를 끈 파일이 그대로 들어온다.</summary>
    public IReadOnlyList<string>? ExcludeFiles { get; init; }

    /// <summary>이 시간을 넘으면 에이전트 프로세스를 끝낸다. 비우면 600초, 1~3600초까지 받는다.</summary>
    public int? TimeoutSeconds { get; init; }

}

/// <param name="Code">오류 이벤트의 분류 코드. UI가 이 값으로 번역하므로 텍스트 접두사를 파싱하지 않아도 된다.</param>
public sealed record AskEvent(string Type, string? Text, string? Code = null);

public sealed record EditorResponse(bool Opened, string FallbackUri, string? Error);

using TraceLens.Context;

namespace TraceLens.Server.Endpoints;

public static class ContextEndpoints
{
    public static RouteGroupBuilder MapContextEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/context", BuildAsync);
        group.MapPost("/report", BuildReportAsync);
        return group;
    }

    /// <summary>
    /// 선택과 목적에서 문맥 DTO와 같은 DTO로 만든 Markdown을 함께 돌려준다.
    /// UI 미리보기·클립보드·인앱 실행·MCP가 모두 이 응답 하나를 쓴다.
    /// </summary>
    internal static async Task<IResult> BuildAsync(
        ContextApiRequest request,
        IGraphStore store,
        ContextBuilder builder,
        CancellationToken cancellationToken)
    {
        var session = store.Session;
        if (session is null)
        {
            return ApiResults.NotAnalyzed();
        }

        if (!TryParseSelection(request, out var selection, out var error))
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, error);
        }

        try
        {
            var document = await builder.BuildAsync(
                session.Index,
                session.Source,
                new ContextRequest(
                    selection,
                    ParseEnum(request.Intent, ContextIntent.Explain),
                    ParseEnum(request.Budget, ContextBudget.Balanced))
                {
                    Question = request.Question ?? string.Empty,
                    ExpectedSnapshotId = request.ExpectedSnapshotId,
                    IncludeSource = request.IncludeSource ?? true,
                    ExcludeFiles = request.ExcludeFiles ?? []
                },
                cancellationToken);
            return Results.Ok(document);
        }
        catch (ContextException exception)
        {
            return ApiResults.FromContextException(exception);
        }
    }

    /// <summary>선택 범위만 담는 공유용 Markdown 보고서. 절대 경로와 소스 본문은 넣지 않는다.</summary>
    internal static async Task<IResult> BuildReportAsync(
        ReportApiRequest request,
        IGraphStore store,
        ContextBuilder builder,
        ShareReportBuilder reportBuilder,
        CancellationToken cancellationToken)
    {
        var session = store.Session;
        if (session is null)
        {
            return ApiResults.NotAnalyzed();
        }

        if (!TryParseSelection(request, out var selection, out var error))
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, error);
        }

        try
        {
            var context = await builder.BuildAsync(
                session.Index,
                session.Source,
                new ContextRequest(selection, ContextIntent.Share, ContextBudget.Detailed) { IncludeSource = false },
                cancellationToken);
            var report = reportBuilder.Build(
                session.Index,
                context,
                new ShareReportRequest(selection)
                {
                    IncludePaths = request.IncludePaths ?? true,
                    IncludeStorageNames = request.IncludeStorageNames ?? true,
                    Title = request.Title ?? string.Empty
                });
            return Results.Ok(report);
        }
        catch (ContextException exception)
        {
            return ApiResults.FromContextException(exception);
        }
    }

    private static bool TryParseSelection(ISelectionRequest request, out ContextSelection selection, out string error)
    {
        selection = new ContextSelection(SelectionKind.Node, string.Empty);
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            error = "selection.id가 필요하다.";
            return false;
        }

        if (!Enum.TryParse<SelectionKind>(request.Kind, ignoreCase: true, out var kind))
        {
            error = $"지원하지 않는 선택 종류다: {request.Kind}. " +
                    $"쓸 수 있는 값: {string.Join(", ", Enum.GetNames<SelectionKind>())}";
            return false;
        }

        selection = new ContextSelection(kind, request.Id.Trim());
        error = string.Empty;
        return true;
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}

public interface ISelectionRequest
{
    string Kind { get; }

    string Id { get; }
}

public sealed record ContextApiRequest(string Kind, string Id) : ISelectionRequest
{
    public string? Intent { get; init; }

    public string? Budget { get; init; }

    public string? Question { get; init; }

    public string? ExpectedSnapshotId { get; init; }

    public bool? IncludeSource { get; init; }

    public IReadOnlyList<string>? ExcludeFiles { get; init; }
}

public sealed record ReportApiRequest(string Kind, string Id) : ISelectionRequest
{
    public bool? IncludePaths { get; init; }

    public bool? IncludeStorageNames { get; init; }

    public string? Title { get; init; }
}

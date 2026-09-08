using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraceLens.Server.Endpoints;

public static class AnalysisJobEndpoints
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static RouteGroupBuilder MapAnalysisJobEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/analysis-jobs", Start);
        group.MapGet("/analysis-jobs/{id}", Get);
        group.MapGet("/analysis-jobs/{id}/events", StreamAsync);
        group.MapDelete("/analysis-jobs/{id}", Cancel);
        return group;
    }

    /// <summary>분석을 작업으로 시작한다. 기존 <c>POST /api/analyze</c>는 호환 경로로 그대로 둔다.</summary>
    internal static IResult Start(AnalyzeRequest request, IAnalysisJobService jobs)
    {
        if (string.IsNullOrWhiteSpace(request.SolutionPath))
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, ApiResults.InvalidRequest, "solutionPath가 필요하다.");
        }

        var job = jobs.Start(request.SolutionPath.Trim());
        return Results.Accepted($"/api/analysis-jobs/{job.Id}", job);
    }

    internal static IResult Get(string id, IAnalysisJobService jobs) =>
        jobs.Get(id) is { } job
            ? Results.Ok(job)
            : ApiResults.Error(StatusCodes.Status404NotFound, "JOB_NOT_FOUND", $"작업을 찾을 수 없다: {id}");

    internal static async Task StreamAsync(
        string id,
        HttpContext context,
        IAnalysisJobService jobs,
        CancellationToken cancellationToken)
    {
        if (jobs.Get(id) is null)
        {
            await ApiResults
                .Error(StatusCodes.Status404NotFound, "JOB_NOT_FOUND", $"작업을 찾을 수 없다: {id}")
                .ExecuteAsync(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var item in jobs.WatchAsync(id, cancellationToken))
            {
                await context.Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(item, EventJsonOptions)}\n\n",
                    cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 브라우저가 연결을 닫았다. 알릴 대상이 없다.
        }
    }

    /// <summary>취소를 요청한다. 취소가 끝날 때까지 기존 그래프는 그대로 둔다.</summary>
    internal static IResult Cancel(string id, IAnalysisJobService jobs) =>
        jobs.Cancel(id)
            ? Results.Ok(new { canceled = id })
            : ApiResults.Error(
                StatusCodes.Status409Conflict,
                "JOB_NOT_CANCELABLE",
                "이미 끝났거나 없는 작업이다.");
}

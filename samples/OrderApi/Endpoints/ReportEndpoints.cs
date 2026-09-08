using OrderApi.Services;

namespace OrderApi.Endpoints;

/// <summary>
/// Minimal API 등록을 확장 메서드 안에 숨긴 형태다.
/// TraceLens는 <c>app.MapReportEndpoints()</c>의 수신 객체를 따라가 라우트 접두사를 합친다.
/// </summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/reports");

        // 메서드 그룹 핸들러
        group.MapGet("/{customerId}", GetSummaryAsync);

        // 람다 핸들러
        group.MapGet("/{customerId}/short", async (
            string customerId,
            IReportService reports,
            CancellationToken cancellationToken) =>
        {
            var summary = await reports.SummarizeAsync(customerId, cancellationToken);
            return Results.Ok(summary[..Math.Min(20, summary.Length)]);
        });

        return builder;
    }

    private static async Task<IResult> GetSummaryAsync(
        string customerId,
        IReportService reports,
        CancellationToken cancellationToken) =>
        Results.Ok(await reports.SummarizeAsync(customerId, cancellationToken));
}

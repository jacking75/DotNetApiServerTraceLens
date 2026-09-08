using OrderApi.Repositories;

namespace OrderApi.Services;

public interface IReportService
{
    Task<string> SummarizeAsync(string customerId, CancellationToken cancellationToken);
}

public sealed class ReportService(IOrderRepository orders) : IReportService
{
    public async Task<string> SummarizeAsync(string customerId, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(customerId, cancellationToken);
        return Describe(order?.Status ?? "none");

        // 로컬 함수도 별도의 실행 단위로 잡힌다.
        static string Describe(string status) => $"상태: {status}";
    }
}

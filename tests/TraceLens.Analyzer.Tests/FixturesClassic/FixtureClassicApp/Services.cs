namespace FixtureClassicApp.Services;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

public interface IReportService
{
    string Summarize(int customerId);
}

public sealed class ReportService(IClock clock) : IReportService
{
    public string Summarize(int customerId) => $"{customerId}:{clock.Now:O}";
}

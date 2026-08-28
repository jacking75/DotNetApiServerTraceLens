namespace FixtureApp.Services;

public sealed class AuditService : IAuditService
{
    public Task RecordAsync(int id) => Task.CompletedTask;
}

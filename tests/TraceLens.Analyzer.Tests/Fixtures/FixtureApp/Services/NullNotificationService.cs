namespace FixtureApp.Services;

/// <summary>DI에 등록하지 않은 두 번째 구현체. 등록된 구현체로만 엣지가 이어지는지 검증한다.</summary>
public sealed class NullNotificationService : INotificationService
{
    public Task NotifyAsync(int id) => Task.CompletedTask;
}

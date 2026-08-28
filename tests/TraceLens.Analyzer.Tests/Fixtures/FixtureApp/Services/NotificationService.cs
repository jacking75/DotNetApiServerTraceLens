namespace FixtureApp.Services;

public sealed class NotificationService : INotificationService
{
    public Task NotifyAsync(int id) => Task.CompletedTask;
}

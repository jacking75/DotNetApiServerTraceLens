namespace OrderApi.Services;

public interface INotificationService
{
    Task SendAsync(string target, string message, CancellationToken cancellationToken);
}

public sealed class NotificationService(ILogger<NotificationService> logger) : INotificationService
{
    public Task SendAsync(string target, string message, CancellationToken cancellationToken)
    {
        logger.LogInformation("알림 {Target}: {Message}", target, message);
        return Task.CompletedTask;
    }
}

namespace FixtureApp.Services;

/// <summary>구현체가 둘이라 DI 등록으로만 구현체를 특정할 수 있는 인터페이스다.</summary>
public interface INotificationService
{
    Task NotifyAsync(int id);
}

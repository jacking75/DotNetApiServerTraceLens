using FixtureApp.Services;

namespace FixtureApp;

/// <summary>Program.cs가 아닌 파일에서 AddSingleton으로 등록하는 경우를 검증하기 위한 확장 메서드다.</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddFixtureServices(this IServiceCollection services) =>
        services.AddSingleton<INotificationService, NotificationService>();
}

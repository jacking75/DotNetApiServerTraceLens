using FixtureClassicApp.Services;

namespace FixtureClassicApp;

/// <summary>클래식 호스트 구성. 수신자 이름이 builder.Services가 아니라 매개변수 이름이다.</summary>
public sealed class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IReportService, ReportService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddControllers();
    }

    public void Configure(IApplicationBuilder application, IWebHostEnvironment environment)
    {
        application.UseRouting();
        application.UseAuthentication();
        application.UseAuthorization();
        application.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}

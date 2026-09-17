namespace FixtureClassicApp;

/// <summary>
/// .NET Core 3.1~5 구조다. Program.cs에는 호스트 빌더만 있고 등록·파이프라인은 Startup.cs에 있다.
/// 여기서 멈추면 시작 흐름과 미들웨어 보기가 통째로 빈다.
/// </summary>
public static class Program
{
    public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());
}

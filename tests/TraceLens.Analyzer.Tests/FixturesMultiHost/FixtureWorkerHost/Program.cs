// 두 번째 실행 프로젝트. 등록도 파이프라인도 API 호스트와 다르다.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<FixtureWorkerHost.JobQueue>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapPost("/jobs", (FixtureWorkerHost.JobQueue queue) => queue.Enqueue("job"));
app.Run();

namespace FixtureWorkerHost
{
    public sealed class JobQueue
    {
        public string Enqueue(string name) => name;
    }
}

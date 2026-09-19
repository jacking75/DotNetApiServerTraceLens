// 실행 프로젝트가 둘인 솔루션의 첫 번째 호스트다(T65).
// 두 호스트의 파이프라인이 서로 다르다 — 첫 프로젝트만 읽으면 두 번째의 인증 미들웨어가 통째로 사라진다.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<FixtureApiHost.OrderCatalog>();

var app = builder.Build();
app.UseHttpsRedirection();
app.MapGet("/api/orders", (FixtureApiHost.OrderCatalog catalog) => catalog.List());
app.Run();

namespace FixtureApiHost
{
    public sealed class OrderCatalog
    {
        public IReadOnlyList<string> List() => ["a", "b"];
    }
}

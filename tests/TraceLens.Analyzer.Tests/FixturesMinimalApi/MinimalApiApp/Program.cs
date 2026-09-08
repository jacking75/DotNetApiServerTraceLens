using MinimalApiApp.Endpoints;
using MinimalApiApp.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IAuditService, AuditService>();

var app = builder.Build();

// 지역 변수에 담은 그룹 + 람다 핸들러
var api = app.MapGroup("/api");
api.MapGet("/ping", (ICatalogService catalog) => Results.Ok(catalog.Describe("ping")));

// 같은 핸들러를 두 라우트에 건다(엔드포인트 ID는 달라야 한다).
api.MapGet("/alias", Handle);
api.MapGet("/alias2", Handle);

// 확장 메서드 안에 숨은 등록. 접두사는 여기서 정해진다.
app.MapGroup("/api/v2").MapCatalogEndpoints();

// 라우트가 상수가 아닌 경우 — 지원하지 않고 추정으로 남겨야 한다.
var dynamicRoute = $"/dynamic/{Environment.MachineName}";
app.MapGet(dynamicRoute, () => Results.Ok());

// 이름만 같은 사용자 메서드는 진입점이 아니다.
var fake = new FakeRouter();
fake.MapGet("/not-an-endpoint", () => { });

app.Run();

static IResult Handle(ICatalogService catalog) => Results.Ok(catalog.Describe("alias"));

internal sealed class FakeRouter
{
    public void MapGet(string pattern, Action handler)
    {
        _ = pattern;
        handler();
    }
}

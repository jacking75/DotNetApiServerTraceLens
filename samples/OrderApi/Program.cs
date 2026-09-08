using OrderApi.Cache;
using OrderApi.Contracts;
using OrderApi.Data;
using OrderApi.Endpoints;
using OrderApi.Middleware;
using OrderApi.Repositories;
using OrderApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IDbGateway, DbGateway>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<ILedgerRepository, LedgerRepository>();
builder.Services.AddSingleton<IOrderCache, OrderCache>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IReportService, ReportService>();

var app = builder.Build();

app.UseMiddleware<RequestLogMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

// Minimal API: MapGroup 접두사 + 람다 핸들러
var api = app.MapGroup("/api/v1");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
api.MapPost("/orders", async (
    CreateOrderRequest request,
    IOrderService orders,
    CancellationToken cancellationToken) =>
{
    var orderId = await orders.CreateAsync(request, cancellationToken);
    return Results.Created($"/api/v1/orders/{orderId}", orderId);
});

// Minimal API: 확장 메서드 안에 숨은 등록. 접두사는 여기서 정해진다.
app.MapGroup("/api/v1").MapReportEndpoints();

app.Run();

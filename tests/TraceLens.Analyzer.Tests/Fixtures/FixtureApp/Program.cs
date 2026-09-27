using FixtureApp;
using FixtureApp.Repositories;
using FixtureApp.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddFixtureServices();

ConfigureLogger();
var app = builder.Build();
if (!await LoadStartupData())
{
    return;
}
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
await LoadStartupData();
app.MapControllers();
app.Run();

void ConfigureLogger() { }
Task<bool> LoadStartupData() => Task.FromResult(true);

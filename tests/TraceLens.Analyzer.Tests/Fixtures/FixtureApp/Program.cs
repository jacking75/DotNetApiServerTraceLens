using FixtureApp.Repositories;
using FixtureApp.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

var app = builder.Build();
app.MapControllers();
app.Run();

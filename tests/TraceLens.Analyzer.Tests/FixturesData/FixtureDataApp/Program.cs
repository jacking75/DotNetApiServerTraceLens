using FixtureDataApp.Cache;
using FixtureDataApp.Data;
using FixtureDataApp.Middleware;
using FixtureDataApp.Services;
using FixtureDataApp.Support;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddCors();
builder.Services.AddAuthorization();

builder.Services.AddScoped<IUserContext, UserContext>();
builder.Services.AddSingleton<IGameCache, RedisGameCache>();
builder.Services.AddSingleton<IMaintenanceConfig, MaintenanceConfig>();
builder.Services.AddSingleton<IPushClient, PushClient>();
builder.Services.AddSingleton<IBillingClient, BillingClient>();
builder.Services.AddScoped<IShardConnectionFactory, ShardConnectionFactory>();
builder.Services.AddScoped<IReplicaConnectionFactory, ReplicaConnectionFactory>();
builder.Services.AddScoped<ILogConnectionFactory, LogConnectionFactory>();
builder.Services.AddSingleton<FakeConnection>();   // 커넥션을 Singleton으로 등록한다 — CONN-002 검증용이다.
builder.Services.AddScoped<ItemRepository>();
builder.Services.AddScoped<CurrencyRepository>();
builder.Services.AddScoped<GuildRepository>();
builder.Services.AddScoped<AttendanceRepository>();
builder.Services.AddScoped<MailRepository>();
builder.Services.AddScoped<LogRepository>();
builder.Services.AddScoped<RankingRepository>();
builder.Services.AddScoped<NoticeRepository>();
builder.Services.AddScoped<LegacyStatRepository>();
builder.Services.AddScoped<CraftService>();
builder.Services.AddScoped<GuildService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<ShopService>();
builder.Services.AddScoped<MailSendService>();
builder.Services.AddScoped<MailReceiveService>();
builder.Services.AddScoped<FriendService>();
builder.Services.AddScoped<RankingService>();

var app = builder.Build();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseHttpsRedirection();
app.UseCors();            // UseRouting 앞이라 엔드포인트별 CORS 정책이 적용되지 않는다 — MW-003.
app.UseRouting();
app.UseMiddleware<RateLimitMiddleware>();
app.UseAuthorization();   // UseAuthentication 보다 먼저다 — MW-002.
app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseMiddleware<MaintenanceMiddleware>();
app.MapControllers();
app.UseMiddleware<LegacyHeaderMiddleware>();   // 엔드포인트 매핑 뒤라 실행되지 않는다 — MW-012.
app.Run();

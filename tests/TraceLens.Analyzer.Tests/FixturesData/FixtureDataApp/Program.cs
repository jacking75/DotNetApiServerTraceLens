using FixtureDataApp.Cache;
using FixtureDataApp.Data;
using FixtureDataApp.Middleware;
using FixtureDataApp.Services;
using FixtureDataApp.Support;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers(options =>
{
    // 전역 MVC 필터. 미들웨어가 아니라 엔드포인트 실행 시점에 돈다(T71).
    options.Filters.Add<AuditActionFilter>();
});
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
builder.Services.AddSingleton<StackExchange.Redis.IDatabase, StackExchange.Redis.FakeDatabase>();
builder.Services.AddSingleton<SqlKata.Execution.QueryFactory>();
builder.Services.AddScoped<GameDbBase>();
builder.Services.AddScoped<BuilderOrderRepository>();
builder.Services.AddScoped<LedgerRepository>();
builder.Services.AddScoped<BuilderOrderService>();
builder.Services.AddSingleton<SessionCache>();
builder.Services.AddScoped<SessionService>();

var app = builder.Build();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseHttpsRedirection();
app.UseCors();            // UseRouting 앞이라 엔드포인트별 CORS 정책이 적용되지 않는다 — MW-003.
app.UseRouting();
app.UseMiddleware<RateLimitMiddleware>();
app.UseAuthorization();   // UseAuthentication 보다 먼저다 — MW-002.
app.UseAuthentication();
app.UseFixtureAuth();     // 솔루션 안 확장 메서드다 — Extension으로 분류하고 하위 ApiKeyMiddleware를 펼쳐야 한다.
app.UseMiddleware<SessionValidationMiddleware>();
app.UseMiddleware<MaintenanceMiddleware>();
app.MapControllers();
app.UseMiddleware<LegacyHeaderMiddleware>();   // MapControllers는 엔드포인트 "등록"이라 이 미들웨어는 정상 실행된다 — MW-012가 나면 안 된다.
app.UseEndpoints(_ => { });                    // 여기서 요청이 끝나는 종단이다.
app.UseMiddleware<DeadHeaderMiddleware>();     // 종단 뒤라 실행되지 않는다 — MW-012.
app.Run();

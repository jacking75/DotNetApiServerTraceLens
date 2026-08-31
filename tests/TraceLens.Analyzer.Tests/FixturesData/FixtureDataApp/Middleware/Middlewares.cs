using FixtureDataApp.Cache;
using FixtureDataApp.Support;

namespace FixtureDataApp.Middleware;

/// <summary>아래 전부를 감싸 예외를 오류 응답으로 바꾼다.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("{\"error\":\"internal\"}");
        }
    }
}

/// <summary>요청에 TraceId를 붙이고, 응답이 돌아올 때 소요 시간을 헤더로 넣는다(MW-006).</summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Items["TraceId"] = Guid.NewGuid().ToString("n");
        var started = Environment.TickCount64;
        await next(context);
        context.Response.Headers["X-Elapsed"] = (Environment.TickCount64 - started).ToString();
        logger.LogInformation("{Path} {Status}", context.Request.Path, context.Response.StatusCode);
    }
}

/// <summary>분당 호출 수를 세어 한도를 넘으면 429로 끊는다. 생성자에 Scoped 서비스를 받는다(MW-004).</summary>
public sealed class RateLimitMiddleware(RequestDelegate next, IUserContext userContext, IGameCache cache)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var key = $"ratelimit:{userContext.UserNo}";
        var current = await cache.GetAsync(key);
        if (current is not null && current.Length > 3)
        {
            context.Response.StatusCode = 429;
            await context.Response.WriteAsync("too many requests");
            return;
        }

        await cache.SetAsync(key, "1", TimeSpan.FromMinutes(1));
        await next(context);
    }
}

/// <summary>세션을 확인해 UserNo를 컨텍스트에 넣는다. 형식이 틀린 분기에서 조용히 빠져나간다(MW-005).</summary>
public sealed class SessionValidationMiddleware(RequestDelegate next, IGameCache cache)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var raw = context.Request.Headers["Authorization"].ToString();
        if (raw.Length == 0)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("unauthorized");
            return;
        }

        if (!raw.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            // next도 부르지 않고 응답도 쓰지 않는다 — 빈 200이 나간다.
            return;
        }

        var session = await cache.GetAsync($"session:{raw}");
        context.Items["UserNo"] = session ?? "0";
        await next(context);
    }
}

/// <summary>점검 모드면 503으로 끊는다. 설정 조회를 동기로 기다린다(MW-008).</summary>
public sealed class MaintenanceMiddleware(RequestDelegate next, IMaintenanceConfig config)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var maintenance = config.IsMaintenanceAsync().Result;
        if (maintenance)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("maintenance");
            return;
        }

        await next(context);
    }
}

/// <summary>엔드포인트 매핑 뒤에 등록되어 실행되지 않는다(MW-012).</summary>
public sealed class LegacyHeaderMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Legacy-Api"] = "1";
        await next(context);
    }
}

namespace OrderApi.Middleware;

/// <summary>요청/응답 구간이 나뉘는 전형적인 미들웨어다. TraceLens의 "미들웨어" 보기에서 pre/post로 나뉘어 보인다.</summary>
public sealed class RequestLogMiddleware(RequestDelegate next, ILogger<RequestLogMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        logger.LogInformation("요청 시작 {Method} {Path}", context.Request.Method, context.Request.Path);
        await next(context);
        logger.LogInformation("요청 끝 {StatusCode}", context.Response.StatusCode);
    }
}

/// <summary>조건이 맞지 않으면 401로 단락한다. "단락 지점" 목록에 이 위치가 나온다.</summary>
public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api") &&
            !context.Request.Headers.ContainsKey("X-Api-Key"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}

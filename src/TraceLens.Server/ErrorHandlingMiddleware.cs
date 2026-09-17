using System.Net;

namespace TraceLens.Server;

/// <summary>
/// 잡지 않은 예외를 <c>{ code, message, details.errorId }</c>로 바꾸고 같은 <c>errorId</c>를 로그에도 남긴다.
/// 사용자는 화면에 뜬 8자만 알려 주면 되고, 로그에서 그 한 줄을 찾을 수 있다.
///
/// 함께 <b>Origin 검사</b>도 한다. 루프백이어도 브라우저의 다른 페이지가 분석 시작이나 CLI 실행을 부를 수 있다.
/// MCP는 stdio라 이 검사와 무관하다.
/// </summary>
public static class RequestGuards
{
    public const string ForbiddenOrigin = "FORBIDDEN_ORIGIN";
    public const string Unexpected = "UNEXPECTED_ERROR";

    /// <summary>개발 중 Vite 개발 서버에서 오는 요청도 허용한다.</summary>
    public static IApplicationBuilder UseTraceLensGuards(this IApplicationBuilder app, int port, int devPort)
    {
        var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"http://127.0.0.1:{port}",
            $"http://localhost:{port}",
            $"http://localhost:{devPort}",
            $"http://127.0.0.1:{devPort}"
        };

        app.Use(async (context, next) =>
        {
            if (!IsAllowed(context, allowedOrigins))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                await context.Response.WriteAsJsonAsync(new ApiError(
                    ForbiddenOrigin,
                    "다른 페이지에서 온 요청은 받지 않는다. TraceLens 화면에서만 쓸 수 있다."));
                return;
            }

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("TraceLens.Server.Request");
            try
            {
                await next();
            }
            catch (Exception exception)
            {
                var errorId = Guid.NewGuid().ToString("N")[..8];
                logger.LogError(exception, "요청 처리 중 오류 {ErrorId}: {Path}", errorId, context.Request.Path);
                if (context.Response.HasStarted)
                {
                    throw;
                }

                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.Response.WriteAsJsonAsync(new ApiError(
                    Unexpected,
                    "요청을 처리하지 못했다. 로그에서 이 errorId로 자세한 내용을 찾을 수 있다.",
                    new { errorId }));
            }
        });
        return app;
    }

    /// <summary>
    /// 상태를 바꾸는 요청(GET/HEAD가 아닌 <c>/api/*</c>)만 검사한다.
    /// <c>Sec-Fetch-Site</c>가 same-origin·none이거나 Origin이 이 서버 자신이면 통과한다.
    /// </summary>
    private static bool IsAllowed(HttpContext context, IReadOnlySet<string> allowedOrigins)
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
            HttpMethods.IsGet(context.Request.Method) ||
            HttpMethods.IsHead(context.Request.Method) ||
            HttpMethods.IsOptions(context.Request.Method))
        {
            return true;
        }

        var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (site is "same-origin" or "none")
        {
            return true;
        }

        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0)
        {
            return allowedOrigins.Contains(origin);
        }

        // Sec-Fetch-Site도 Origin도 없는 요청은 브라우저가 아니다(curl·CLI·테스트). 로컬 전용이므로 통과시킨다.
        return site.Length == 0;
    }
}

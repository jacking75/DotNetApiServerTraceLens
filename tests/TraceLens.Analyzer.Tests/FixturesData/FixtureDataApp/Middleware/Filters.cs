using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FixtureDataApp.Middleware;

/// <summary>
/// 전역 필터. `AddControllers(o =&gt; o.Filters.Add&lt;AuditActionFilter&gt;())`로 등록한다(T71).
/// </summary>
public sealed class AuditActionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        // 요청 구간에서 하는 일.
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        // 응답 구간에서 하는 일.
    }
}

/// <summary>액션에 특성으로 붙이는 필터. `context.Result = …`로 단락한다.</summary>
public sealed class RequireHeaderFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.Request.Headers.ContainsKey("X-Api-Key"))
        {
            context.Result = new UnauthorizedResult();
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}

/// <summary>예외 필터. 예외를 응답으로 바꾼다.</summary>
public sealed class DomainExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result = new BadRequestResult();
    }
}

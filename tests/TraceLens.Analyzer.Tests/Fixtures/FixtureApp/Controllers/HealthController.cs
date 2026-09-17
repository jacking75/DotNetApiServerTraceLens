using FixtureApp.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace FixtureApp.Controllers;

/// <summary>클래스 라우트를 기반 클래스에 두고, 메서드 라우트 하나는 절대 경로다.</summary>
[Route("api/v1/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
}

[ApiController]
public sealed class HealthController(Repository<OrderRow> repository) : ApiControllerBase
{
    /// <summary>절대 경로다. 클래스 접두사를 붙이면 안 된다 — /health여야 한다.</summary>
    [HttpGet("/health")]
    public IActionResult Health() => Ok("ok");

    /// <summary>상대 경로다. 기반 클래스의 [Route]가 접두사가 된다 — /api/v1/health/orders/{id}.</summary>
    [HttpGet("orders/{id:int}")]
    public async Task<IActionResult> GetOrder(int id)
    {
        // 제네릭 저장소 호출과 확장 메서드 호출. 노드 ID가 선언 기준으로 정규화돼야 엣지가 살아남는다.
        var direct = await repository.GetAsync(id);
        var viaExtension = await repository.GetOrDefaultAsync(id);
        return Ok(new { direct, viaExtension });
    }
}

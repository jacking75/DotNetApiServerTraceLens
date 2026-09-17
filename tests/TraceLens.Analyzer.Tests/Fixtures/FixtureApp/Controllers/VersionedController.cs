using Microsoft.AspNetCore.Mvc;

namespace FixtureApp.Controllers;

/// <summary>
/// 버전 토큰이 들어간 라우트와 <c>[AcceptVerbs]</c>·<c>[NonAction]</c>을 함께 담은 컨트롤러다.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class VersionedController : ControllerBase
{
    /// <summary>동사가 둘이다. 라우트 하나에 엔드포인트가 둘 나와야 한다.</summary>
    [AcceptVerbs("GET", "HEAD", Route = "items")]
    public IActionResult Items() => Ok();

    /// <summary>라우팅 대상이 아니다. 진입점 목록에 나오면 안 된다.</summary>
    [NonAction]
    [HttpGet("hidden")]
    public IActionResult Hidden() => Ok();
}

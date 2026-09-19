using Microsoft.AspNetCore.Mvc;

namespace FixtureClassicApp.Controllers;

/// <summary>
/// 메서드 <c>[Route]</c>·HEAD/OPTIONS·비추상 기반 컨트롤러·<c>IgnoreApi</c>의 회귀 픽스처다(T54).
/// 예전에는 메서드 <c>[Route]</c>를 읽지 않아 존재하지 않는 라우트가 "확실"로 나왔다.
/// </summary>
[ApiController]
[Route("api/orders")]
public sealed class OrderRoutingController : ControllerBase
{
    /// <summary>진입 특성에 템플릿이 없고 메서드 [Route]만 있다 — /api/orders/items여야 한다.</summary>
    [Route("items")]
    [HttpGet]
    public IActionResult Items() => Ok();

    /// <summary>
    /// 둘 다 있으면 서로를 대체하지 않고 각각 별도 라우트가 된다.
    /// /api/orders/summary 와 /api/orders/legacy-summary 둘 다 나와야 한다.
    /// </summary>
    [Route("legacy-summary")]
    [HttpGet("summary")]
    public IActionResult Summary() => Ok();

    /// <summary>HEAD·OPTIONS도 실제 라우트다.</summary>
    [HttpHead("ping")]
    public IActionResult Ping() => Ok();

    [HttpOptions("ping")]
    public IActionResult PingOptions() => Ok();

    /// <summary>문서에서 숨겼지만 라우트는 존재한다. 목록에서 지우지 않고 구분만 한다.</summary>
    [ApiExplorerSettings(IgnoreApi = true)]
    [HttpGet("internal")]
    public IActionResult Internal() => Ok();
}

/// <summary>
/// **비추상** 기반 컨트롤러. 이 클래스 자체도 라우팅 가능하므로 기반 라우트와 파생 라우트가 모두 실재한다.
/// 예전에는 `IsAbstract`일 때만 파생을 만들어 파생 라우트가 통째로 빠졌다.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SearchControllerBase : ControllerBase
{
    [HttpGet("query")]
    public IActionResult Query() => Ok();
}

[ApiController]
public sealed class ProductSearchController : SearchControllerBase
{
}

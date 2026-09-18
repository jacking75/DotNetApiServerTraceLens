using Microsoft.AspNetCore.Mvc;

namespace FixtureClassicApp.Controllers;

/// <summary>
/// 기반 클래스에 액션을 선언한 흔한 형태다.
/// 이 클래스 이름으로 라우트를 만들면 `/api/CrudControllerBase/{id}` 라는 존재하지 않는 경로가
/// "확실"로 나온다. 파생 컨트롤러마다 하나씩 나와야 한다.
/// </summary>
[Route("api/[controller]")]
public abstract class CrudControllerBase<T> : ControllerBase
{
    [HttpGet("{id:int}")]
    public IActionResult GetById(int id) => Ok(id);
}

[ApiController]
public sealed class ProductsController : CrudControllerBase<int>
{
}

[ApiController]
public sealed class CustomersController : CrudControllerBase<int>
{
}

/// <summary>버전 토큰이 라우트에 있다. v1·v2가 각각 나와야 한다.</summary>
[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/catalog")]
public sealed class CatalogController : ControllerBase
{
    [HttpGet("items")]
    public IActionResult Items() => Ok();
}

/// <summary>버전 토큰이 없다(헤더·쿼리 버전). 라우트는 같고 endpointId로만 구분돼야 한다.</summary>
[ApiController]
[ApiVersion("3.0")]
[Route("api/legacy")]
public sealed class LegacyController : ControllerBase
{
    [HttpGet("items")]
    public IActionResult Items() => Ok();
}

/// <summary>`[ApiVersion]` 특성을 패키지 없이 흉내 낸다. 판정은 특성 이름으로만 한다.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class ApiVersionAttribute(string version) : Attribute
{
    public string Version { get; } = version;
}

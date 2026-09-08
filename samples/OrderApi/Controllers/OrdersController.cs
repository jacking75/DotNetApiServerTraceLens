using Microsoft.AspNetCore.Mvc;
using OrderApi.Contracts;
using OrderApi.Services;

namespace OrderApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController(IOrderService orders) : ControllerBase
{
    [HttpGet("{orderId}")]
    public async Task<ActionResult<OrderView>> GetAsync(string orderId, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    public async Task<ActionResult<string>> CreateAsync(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await orders.CreateAsync(request, cancellationToken));

    /// <summary>
    /// 같은 메서드에 라우트를 두 개 걸었다. TraceLens는 엔드포인트 ID를 핸들러 노드 ID와 분리해서
    /// 이 둘을 각각 다른 진입점으로 보여 준다.
    /// </summary>
    [HttpPost("{orderId}/ship")]
    [HttpPost("{orderId}/dispatch")]
    public async Task<IActionResult> ShipAsync(
        string orderId,
        [FromBody] ShipOrderRequest request,
        CancellationToken cancellationToken)
    {
        await orders.ShipAsync(orderId, request, cancellationToken);
        return Accepted();
    }
}

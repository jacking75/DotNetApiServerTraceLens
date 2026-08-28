using FixtureApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixtureApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController(
    IOrderService orderService,
    INotificationService notificationService,
    IAuditService auditService) : ControllerBase
{
    /// <summary>여러 곳을 호출하는 진입점. 첫 화면에서 호출 대상이 모두 보이는지 검증하는 데 쓴다.</summary>
    [HttpPost("{id:int}")]
    public async Task<IActionResult> Create(int id)
    {
        await orderService.CreateAsync(id);
        await notificationService.NotifyAsync(id);
        await auditService.RecordAsync(id);
        return Ok();
    }
}

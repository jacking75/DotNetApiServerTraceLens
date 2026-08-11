using FixtureApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixtureApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ReportsController(IOrderService orderService) : ControllerBase
{
    [HttpGet("orders/{id:int}")]
    public async Task<IActionResult> Preview(int id)
    {
        await orderService.CreateAsync(id);
        return Ok();
    }
}

using FixtureApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixtureApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpPost("{id:int}")]
    public async Task<IActionResult> Create(int id)
    {
        await orderService.CreateAsync(id);
        return Ok();
    }
}

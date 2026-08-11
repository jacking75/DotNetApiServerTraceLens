using FixtureApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixtureApp.Controllers;

[ApiController]
[Route("api/admin/orders")]
public sealed class AdminController(IOrderService orderService) : ControllerBase
{
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Rebuild(int id)
    {
        await orderService.CreateAsync(id);
        return NoContent();
    }
}

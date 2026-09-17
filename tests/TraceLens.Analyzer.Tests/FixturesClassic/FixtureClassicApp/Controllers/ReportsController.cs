using FixtureClassicApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixtureClassicApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ReportsController(IReportService reports) : ControllerBase
{
    [HttpGet("{customerId}")]
    public IActionResult Get(int customerId) => Ok(reports.Summarize(customerId));
}

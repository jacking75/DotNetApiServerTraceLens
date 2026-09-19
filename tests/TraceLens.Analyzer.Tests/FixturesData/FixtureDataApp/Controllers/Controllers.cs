using FixtureDataApp.Contracts;
using FixtureDataApp.Data;
using FixtureDataApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixtureDataApp.Controllers;

/// <summary>요청 본문의 UserNo를 검증 없이 그대로 넘긴다. SHARD-005 검증용이다.</summary>
[ApiController]
[Route("api/item")]
[TypeFilter(typeof(FixtureDataApp.Middleware.DomainExceptionFilter))]
public sealed class ItemController(CraftService craftService) : ControllerBase
{
    /// <summary>액션에 붙은 필터. 단락(`context.Result = …`)이 있다.</summary>
    [ServiceFilter(typeof(FixtureDataApp.Middleware.RequireHeaderFilter))]
    [HttpPost("craft")]
    public async Task<IActionResult> Craft([FromBody] CraftRequest request)
    {
        await craftService.CraftAsync(request.UserNo, request.ItemId);
        return Ok();
    }
}

/// <summary>인증 컨텍스트에서 사용자 번호를 가져온다. SHARD-005가 걸리지 않아야 하는 정상 사례다.</summary>
[ApiController]
[Route("api/guild")]
public sealed class GuildController(GuildService guildService) : ControllerBase
{
    [HttpPost("join")]
    public async Task<IActionResult> Join(int guildId)
    {
        var userNo = int.Parse(User?.Identity?.Name ?? "0");
        await guildService.JoinAsync(userNo, guildId);
        return Ok();
    }
}

[ApiController]
[Route("api/attendance")]
public sealed class AttendanceController(AttendanceService attendanceService) : ControllerBase
{
    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] AttendanceRequest request)
    {
        await attendanceService.CheckAsync(request.UserNo, request.Day);
        return Ok();
    }
}

[ApiController]
[Route("api/shop")]
public sealed class ShopController(ShopService shopService) : ControllerBase
{
    [HttpPost("buy")]
    public async Task<IActionResult> Buy(int itemId, int price)
    {
        var userNo = int.Parse(User?.Identity?.Name ?? "0");
        await shopService.BuyAsync(userNo, itemId, price);
        return Ok();
    }
}

[ApiController]
[Route("api/mail")]
public sealed class MailController(MailSendService sendService, MailReceiveService receiveService) : ControllerBase
{
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] MailSendRequest request)
    {
        await sendService.SendAsync(request.SenderNo, request.TargetNo, request.Title, request.Cost);
        return Ok();
    }

    [HttpPost("receive")]
    public async Task<IActionResult> Receive()
    {
        var userNo = int.Parse(User?.Identity?.Name ?? "0");
        await receiveService.ReceiveAllAsync(userNo);
        return Ok();
    }
}

[ApiController]
[Route("api/friend")]
public sealed class FriendController(FriendService friendService) : ControllerBase
{
    [HttpGet("list")]
    public async Task<IActionResult> List()
    {
        var userNo = int.Parse(User?.Identity?.Name ?? "0");
        var count = await friendService.GetFriendProfilesAsync(userNo, [1, 2, 3]);
        return Ok(count);
    }
}

[ApiController]
[Route("api/ranking")]
public sealed class RankingController(RankingService rankingService) : ControllerBase
{
    [HttpGet("top")]
    public async Task<IActionResult> Top() => Ok(await rankingService.BuildTopAsync());
}

/// <summary>샤드 번호를 상수로 넘긴다. SHARD-004 검증용이다.</summary>
[ApiController]
[Route("api/dev")]
public sealed class DevToolController(IShardConnectionFactory factory, NoticeRepository noticeRepository) : ControllerBase
{
    [HttpGet("dump")]
    public async Task<IActionResult> Dump([FromQuery] string keyword)
    {
        await using var connection = await factory.OpenAsync(3);
        var rows = await noticeRepository.SearchAsync(connection, keyword);
        return Ok(rows.Count);
    }
}

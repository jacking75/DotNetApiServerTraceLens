namespace FixtureDataApp.Support;

/// <summary>되돌릴 수 없는 외부 발송. 설정의 irreversible 목록에 들어간다.</summary>
public interface IPushClient
{
    Task SendAsync(int userNo, string message);
}

public sealed class PushClient : IPushClient
{
    public Task SendAsync(int userNo, string message) => Task.CompletedTask;
}

/// <summary>재화 지급 외부 API. 트랜잭션 안 외부 호출(TX-001) 검증용이다.</summary>
public interface IBillingClient
{
    Task GrantAsync(int userNo, int amount);
}

public sealed class BillingClient : IBillingClient
{
    public Task GrantAsync(int userNo, int amount) => Task.CompletedTask;
}

/// <summary>요청마다 달라지는 Scoped 서비스. 미들웨어 생성자에 주입되면 MW-004가 걸린다.</summary>
public interface IUserContext
{
    int UserNo { get; }
}

public sealed class UserContext : IUserContext
{
    public int UserNo { get; } = 0;
}

/// <summary>점검 모드 설정. 미들웨어에서 동기로 기다리는 예(MW-008)를 만든다.</summary>
public interface IMaintenanceConfig
{
    Task<bool> IsMaintenanceAsync();
}

public sealed class MaintenanceConfig : IMaintenanceConfig
{
    public Task<bool> IsMaintenanceAsync() => Task.FromResult(false);
}

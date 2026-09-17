namespace FixtureExternalApp.Jobs;

/* ------------------------------------------------------------------ 프레임워크 흉내

   패키지를 참조하지 않고 기반 타입·인터페이스 이름만 맞춘다.
   후보 판정은 이름 휴리스틱이 아니라 기반 타입·구현 인터페이스로 하므로 이걸로 충분하다.       */

public abstract class BackgroundService
{
    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
}

public interface IConsumer<TMessage>
{
    Task Consume(TMessage message);
}

public abstract class Hub
{
}

/// <summary>HTTP 진입점이 아니라 워커다. 진입점으로 세면 안 되고, 세지 않았다는 사실은 남아야 한다.</summary>
public sealed class RankBatchWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}

public sealed record OrderPlaced(int OrderId);

/// <summary>메시지 컨슈머. 같은 이유로 세지 않지만 개수는 알려야 한다.</summary>
public sealed class OrderPlacedConsumer : IConsumer<OrderPlaced>
{
    public Task Consume(OrderPlaced message) => Task.CompletedTask;
}

/// <summary>SignalR Hub. HTTP 위에서 돌지만 라우팅 특성이 없어 진입점으로 잡히지 않는다.</summary>
public sealed class NotificationHub : Hub
{
    public Task Notify(int userId) => Task.CompletedTask;
}

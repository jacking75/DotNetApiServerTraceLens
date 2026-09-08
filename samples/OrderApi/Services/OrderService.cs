using OrderApi.Cache;
using OrderApi.Contracts;
using OrderApi.Repositories;

namespace OrderApi.Services;

public interface IOrderService
{
    Task<OrderView?> GetAsync(string orderId, CancellationToken cancellationToken);

    Task<string> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken);

    Task ShipAsync(string orderId, ShipOrderRequest request, CancellationToken cancellationToken);
}

public sealed class OrderService(
    IOrderRepository orders,
    IInventoryRepository inventory,
    ILedgerRepository ledger,
    IOrderCache cache,
    INotificationService notifications) : IOrderService
{
    public async Task<OrderView?> GetAsync(string orderId, CancellationToken cancellationToken)
    {
        var cached = cache.Get(orderId);
        if (cached is not null)
        {
            return cached;
        }

        var order = await orders.FindAsync(orderId, cancellationToken);
        if (order is not null)
        {
            cache.Set(order);
        }

        return order;
    }

    /// <summary>
    /// 여기가 예제의 핵심이다. 재고 예약 → 주문 저장 → 원장 기록을 트랜잭션 없이 이어서 한다.
    /// 중간에 실패하면 앞 단계가 그대로 남는데, 되돌리는 코드(ReleaseAsync)는 이 경로에 없다.
    /// TraceLens의 "보상 처리" 판정이 이 경로를 어떻게 표시하는지 확인하면 된다.
    /// </summary>
    public async Task<string> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        await inventory.ReserveAsync(request.Sku, request.Quantity, cancellationToken);
        var orderId = await orders.InsertAsync(request, cancellationToken);
        await ledger.AppendAsync(orderId, request.Quantity * 1000m, cancellationToken);
        await notifications.SendAsync(request.CustomerId, $"주문 {orderId}을(를) 접수했다.", cancellationToken);
        return orderId;
    }

    public async Task ShipAsync(string orderId, ShipOrderRequest request, CancellationToken cancellationToken)
    {
        await orders.UpdateStatusAsync(orderId, "shipped", cancellationToken);
        cache.Remove(orderId);
        await notifications.SendAsync(orderId, $"{request.Carrier}(으)로 배송을 시작했다.", cancellationToken);
    }
}

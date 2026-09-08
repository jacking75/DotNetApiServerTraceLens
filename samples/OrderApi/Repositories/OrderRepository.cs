using OrderApi.Contracts;
using OrderApi.Data;

namespace OrderApi.Repositories;

public interface IOrderRepository
{
    Task<OrderView?> FindAsync(string orderId, CancellationToken cancellationToken);

    Task<string> InsertAsync(CreateOrderRequest request, CancellationToken cancellationToken);

    Task UpdateStatusAsync(string orderId, string status, CancellationToken cancellationToken);

    Task DeleteAsync(string orderId, CancellationToken cancellationToken);
}

public sealed class OrderRepository(IDbGateway db) : IOrderRepository
{
    public Task<OrderView?> FindAsync(string orderId, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<OrderView>(
            "SELECT order_id, customer_id, sku, quantity, status FROM orders WHERE order_id = @orderId",
            new { orderId },
            cancellationToken);

    public async Task<string> InsertAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var orderId = Guid.NewGuid().ToString("N")[..10];
        await db.ExecuteAsync(
            "INSERT INTO orders (order_id, customer_id, sku, quantity, status) VALUES (@orderId, @customerId, @sku, @quantity, 'created')",
            new { orderId, request.CustomerId, request.Sku, request.Quantity },
            cancellationToken);
        return orderId;
    }

    public Task UpdateStatusAsync(string orderId, string status, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "UPDATE orders SET status = @status WHERE order_id = @orderId",
            new { orderId, status },
            cancellationToken);

    /// <summary>주문 삽입을 되돌리는 보상 처리다. 지금은 어떤 경로에서도 부르지 않는다.</summary>
    public Task DeleteAsync(string orderId, CancellationToken cancellationToken) =>
        db.ExecuteAsync("DELETE FROM orders WHERE order_id = @orderId", new { orderId }, cancellationToken);
}

public interface IInventoryRepository
{
    Task ReserveAsync(string sku, int quantity, CancellationToken cancellationToken);

    Task ReleaseAsync(string sku, int quantity, CancellationToken cancellationToken);
}

public sealed class InventoryRepository(IDbGateway db) : IInventoryRepository
{
    public Task ReserveAsync(string sku, int quantity, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "UPDATE inventory SET reserved = reserved + @quantity WHERE sku = @sku",
            new { sku, quantity },
            cancellationToken);

    /// <summary>재고 예약을 되돌리는 보상 처리다. CreateAsync의 실패 경로에서 부르지 않는 것이 이 예제의 핵심이다.</summary>
    public Task ReleaseAsync(string sku, int quantity, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "UPDATE inventory SET reserved = reserved - @quantity WHERE sku = @sku",
            new { sku, quantity },
            cancellationToken);
}

public interface ILedgerRepository
{
    Task AppendAsync(string orderId, decimal amount, CancellationToken cancellationToken);
}

/// <summary>append-only 원장이다. 지운다는 개념이 없어 되돌릴 수 없는 단계로 봐야 한다.</summary>
public sealed class LedgerRepository(IDbGateway db) : ILedgerRepository
{
    public Task AppendAsync(string orderId, decimal amount, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "INSERT INTO ledger (order_id, amount) VALUES (@orderId, @amount)",
            new { orderId, amount },
            cancellationToken);
}

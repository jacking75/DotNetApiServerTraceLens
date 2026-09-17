using System.Transactions;
using Microsoft.EntityFrameworkCore;

namespace FixtureExternalApp;

public sealed class Order
{
    public int Id { get; set; }
}

public sealed class ShopContext : DbContext
{
}

/// <summary>
/// EF Core로만 저장하는 코드다. SQL 리터럴도 Dapper 호출도 없으므로 DB 접근이 0건으로 나온다.
/// "DB를 안 쓴다"가 아니라 "이 버전이 읽지 않는다"를 결과가 말해야 한다.
/// </summary>
public sealed class OrderStore(ShopContext context)
{
    public async Task SaveAsync(Order order, CancellationToken cancellationToken)
    {
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        context.Set<Order>().Add(order);
        await context.SaveChangesAsync(cancellationToken);
        scope.Complete();
    }

    public void Remove(Order order)
    {
        context.Set<Order>().Remove(order);
        context.SaveChanges();
    }
}

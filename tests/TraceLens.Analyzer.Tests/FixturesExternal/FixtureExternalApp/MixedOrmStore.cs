using Microsoft.EntityFrameworkCore;

namespace FixtureExternalApp;

/// <summary>테스트 픽스처라 실제 DB에 붙지 않는다. 분석기가 보는 것은 넘기는 SQL 문자열뿐이다.</summary>
public sealed class FakeDbConnection
{
    public int ExecuteScalar(string sql) => 0;
}

/// <summary>
/// 실전에서 가장 흔한 EF Core + Dapper 혼용이다.
/// 접근이 1건이라도 있으면 EF Core <c>DbSet</c>이 몇 곳이든 "EF Core는 안 읽었다"가 사라졌다(T55).
/// 접근이 1건 이상이어도 ORM 힌트가 있으면 notes가 남아야 한다.
/// </summary>
public sealed class MixedOrmStore(ShopContext context, FakeDbConnection connection)
{
    /// <summary>SQL 리터럴 쪽. 이 한 건이 접근으로 잡힌다.</summary>
    public int CountOrders() => connection.ExecuteScalar("SELECT COUNT(*) FROM orders");

    /// <summary>EF Core 쪽. 접근으로 잡히지 않는다 — 그 사실을 notes가 말해야 한다.</summary>
    public void AddOrder(Order order)
    {
        context.Set<Order>().Add(order);
        context.SaveChanges();
    }

    /// <summary>
    /// 식 본문 프로퍼티 안의 쿼리. 예전에는 스캔 단위가 메서드·생성자뿐이라 조용히 빠졌다(T55).
    /// </summary>
    public int PendingCount => connection.ExecuteScalar("SELECT COUNT(*) FROM pending_orders");

    /// <summary>접근자 본문 안의 쿼리도 같다.</summary>
    public int ArchivedCount
    {
        get
        {
            return connection.ExecuteScalar("SELECT COUNT(*) FROM archived_orders");
        }
    }
}

using System.Data;
using SqlKata.Execution;

namespace FixtureDataApp.Data;

/// <summary>
/// 람다를 받아 그 안에서 트랜잭션을 여닫는 래퍼다.
/// 설정에 적지 않아도 "본문에 BeginTransaction이 있고 대리자 파라미터를 부르는 메서드"라서 자동으로 래퍼로 잡혀야 한다.
/// </summary>
public sealed class GameDbBase(QueryFactory factory)
{
    public async Task<T> TransactionAsync<T>(Func<QueryFactory, IDbTransaction, Task<T>> action)
    {
        using var connection = new FakeDbConnection();
        var transaction = connection.BeginTransaction();
        var result = await action(factory, transaction);
        transaction.Commit();
        return result;
    }
}

/// <summary>스텁 커넥션. 분석기가 보는 것은 <c>BeginTransaction</c> 호출뿐이다.</summary>
public sealed class FakeDbConnection : IDisposable
{
    public IDbTransaction BeginTransaction() => new FakeDbTransaction();

    public void Dispose()
    {
    }
}

public sealed class FakeDbTransaction : IDbTransaction
{
    public IDbConnection? Connection => null;

    public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

    public void Commit()
    {
    }

    public void Rollback()
    {
    }

    public void Dispose()
    {
    }
}

public sealed class OrderRow
{
    public long OrderId { get; set; }

    public int UserNo { get; set; }
}

using System.Data;
using SqlKata;
using SqlKata.Execution;

namespace FixtureDataApp.Data;

/// <summary>SQL 리터럴 없이 빌더 체인으로 쿼리한다. T2 검증용이다.</summary>
public sealed class BuilderOrderRepository
{
    public Task<int> InsertOrderAsync(QueryFactory db, IDbTransaction transaction, int userNo, string itemId) =>
        db.Query("player_order").InsertAsync(new { user_no = userNo, item_id = itemId }, transaction);

    /// <summary>테이블 이름이 변수다. 대상을 확정하지 못하므로 추정으로 남아야 한다.</summary>
    public Task<int> UpdateByTableAsync(QueryFactory db, string table, int userNo) =>
        db.Query(table).Where("user_no", userNo).UpdateAsync(new { count = 1 });

    /// <summary>체인을 지역 변수에 담았다가 실행한다. 선언자의 초기화식을 한 단계 따라가야 한다.</summary>
    public Task<OrderRow?> LoadAsync(QueryFactory db, int userNo)
    {
        var query = db.Query("player_order").Where("user_no", userNo);
        return query.FirstOrDefaultAsync<OrderRow>();
    }
}

public sealed class LedgerRepository
{
    public Task<int> AppendAsync(QueryFactory db, IDbTransaction transaction, int userNo) =>
        db.Query("player_ledger").InsertAsync(new { user_no = userNo }, transaction);

    /// <summary>람다 안에서만 불리는 메서드. 람다를 순회하지 않으면 호출자가 없는 것처럼 보인다.</summary>
    public Task AppendFireAndForgetAsync(int userNo) => Task.CompletedTask;
}

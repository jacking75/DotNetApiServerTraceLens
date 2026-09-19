namespace FixtureDataApp.Data;

/// <summary>
/// 트랜잭션 판정의 경계 사례들(T56).
/// 셋 다 예전에는 "확실"로 COMP 오탐을 냈다.
/// </summary>
public sealed class TransactionEdgeCases(IShardConnectionFactory factory)
{
    /// <summary>
    /// (a) 조건부 Commit. 선형 카운터로 세면 `if` 안의 Commit이 그 뒤 코드까지 트랜잭션 밖으로 만든다.
    /// 경계는 `using` 블록 끝까지다 — 커밋 여부와 무관하다.
    /// </summary>
    public async Task ConditionalCommitAsync(int userNo, bool ok)
    {
        await using var connection = await factory.OpenAsync(userNo);
        var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("UPDATE user_stat SET win = win + 1 WHERE user_no = @user_no", new { userNo });
        if (ok)
        {
            await transaction.CommitAsync();
        }

        // 이 쓰기도 같은 경계 안이다. 예전에는 Commit 뒤라는 이유로 트랜잭션 밖으로 셌다.
        await connection.ExecuteAsync("UPDATE user_stat SET lose = lose + 1 WHERE user_no = @user_no", new { userNo });
    }

    /// <summary>
    /// (c) `if/else` 배타 분기의 쓰기 2개. 순차 2단계가 아니라 같은 단계의 다른 경로다.
    /// </summary>
    public async Task ExclusiveBranchesAsync(int userNo, bool grant)
    {
        await using var connection = await factory.OpenAsync(userNo);
        if (grant)
        {
            await connection.ExecuteAsync("INSERT INTO user_flag (user_no) VALUES (@user_no)", new { userNo });
        }
        else
        {
            await connection.ExecuteAsync("DELETE FROM user_flag WHERE user_no = @user_no", new { userNo });
        }
    }
}

/// <summary>
/// (b) 항상 트랜잭션 래퍼 안에서만 불리는 리포지터리.
/// 자체 쓰기가 2개라는 이유로 COMP-001/002(확실)가 나오면 오탐이다.
/// </summary>
public sealed class InventoryRepository
{
    public async Task MoveAsync(FakeConnection connection, int userNo, int itemId)
    {
        await connection.ExecuteAsync("DELETE FROM user_item WHERE user_no = @user_no AND item_id = @item_id", new { userNo, itemId });
        await connection.ExecuteAsync("INSERT INTO user_storage (user_no, item_id) VALUES (@user_no, @item_id)", new { userNo, itemId });
    }
}

/// <summary>InventoryRepository.MoveAsync를 부르는 유일한 자리. 언제나 트랜잭션 경계 안이다.</summary>
public sealed class InventoryService(IShardConnectionFactory factory, InventoryRepository inventory)
{
    public async Task MoveInTransactionAsync(int userNo, int itemId)
    {
        await using var connection = await factory.OpenAsync(userNo);
        var transaction = await connection.BeginTransactionAsync();
        await inventory.MoveAsync(connection, userNo, itemId);
        await transaction.CommitAsync();
    }
}

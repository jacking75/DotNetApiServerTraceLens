using System.Data;
using System.Transactions;

namespace FixtureDataApp.Data;

/// <summary>기본 queryApis 패턴(<c>*DbConnection.Execute*</c>·<c>*DbConnection.Query*</c>)에 맞는 이름이다.</summary>
public sealed class ProcDbConnection
{
    public Task<int> ExecuteAsync(string sql, object? parameters = null, CommandType? commandType = null) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, CommandType? commandType = null)
        where T : new() => Task.FromResult<IReadOnlyList<T>>([]);
}

/// <summary>
/// 저장 프로시저 호출과 <c>TransactionScope</c> 경로다.
/// 둘 다 예전에는 <b>확실</b>로 틀린 판정이 나던 자리다.
/// </summary>
public sealed class ProcedureRepository
{
    /// <summary>이름은 Query로 시작하지만 실제로는 갱신 프로시저다. Read로 단정하면 안 된다.</summary>
    public Task<IReadOnlyList<UserRow>> RunUpdateProcedureAsync(ProcDbConnection connection, int userNo) =>
        connection.QueryAsync<UserRow>("usp_UpdateOrder", new { userNo }, commandType: CommandType.StoredProcedure);

    /// <summary>Execute 계열이라 쓰기 후보로 남기되 확실도는 추정이다.</summary>
    public Task<int> RunWriteProcedureAsync(ProcDbConnection connection, int userNo) =>
        connection.ExecuteAsync("usp_GrantReward", new { userNo }, commandType: CommandType.StoredProcedure);

    /// <summary>
    /// TransactionScope로 감싼 쓰기 두 건이다. 경계를 못 잡으면 "트랜잭션 없는 쓰기"로 COMP 규칙이 오탐한다.
    /// </summary>
    public async Task WriteInsideScopeAsync(FakeConnection connection, int userNo)
    {
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await connection.ExecuteAsync(
            "UPDATE user_currency SET gold = gold - @gold WHERE user_no = @user_no", new { userNo });
        await connection.ExecuteAsync(
            "INSERT INTO user_item (user_no, item_id, count) VALUES (@user_no, 1, 1)", new { userNo });
        scope.Complete();
    }
}

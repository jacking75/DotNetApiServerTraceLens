namespace OrderApi.Data;

/// <summary>
/// 예제용 DB 게이트웨이다. 실제 DB 대신 메모리를 쓰지만 SQL은 호출 인자로 그대로 넘긴다.
/// TraceLens는 이 인자에서 테이블 이름과 연산(SELECT/INSERT/UPDATE/DELETE)을 읽는다.
/// 사내 래퍼를 쓴다면 tracelens.config.json의 <c>db.queryApis</c>에 그 타입을 적어 주면 된다.
/// </summary>
public interface IDbGateway
{
    Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken cancellationToken);

    Task<T?> QuerySingleAsync<T>(string sql, object? parameters, CancellationToken cancellationToken);
}

public sealed class DbGateway : IDbGateway
{
    /// <summary>테이블 이름 → 행. 예제라 메모리로 흉내만 낸다.</summary>
    private static readonly Dictionary<string, Dictionary<string, object>> Tables = new(StringComparer.Ordinal);

    public Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken cancellationToken)
    {
        _ = parameters;
        _ = Tables;
        _ = sql;
        return Task.FromResult(1);
    }

    public Task<T?> QuerySingleAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
    {
        _ = parameters;
        _ = sql;
        return Task.FromResult(default(T));
    }
}

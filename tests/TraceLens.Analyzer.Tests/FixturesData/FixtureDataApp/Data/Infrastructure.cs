namespace FixtureDataApp.Data;

/// <summary>샤드 커넥션을 여는 팩토리. 첫 인자가 샤드 키다.</summary>
public interface IShardConnectionFactory
{
    Task<FakeConnection> OpenAsync(int userNo);
}

public sealed class ShardConnectionFactory : IShardConnectionFactory
{
    public Task<FakeConnection> OpenAsync(int userNo) => Task.FromResult(new FakeConnection($"shard-{userNo % 8}"));
}

/// <summary>읽기 전용 복제본. 설정에서 readOnly로 표시했다.</summary>
public interface IReplicaConnectionFactory
{
    Task<FakeConnection> OpenAsync();
}

public sealed class ReplicaConnectionFactory : IReplicaConnectionFactory
{
    public Task<FakeConnection> OpenAsync() => Task.FromResult(new FakeConnection("replica"));
}

/// <summary>로그 전용 DB.</summary>
public interface ILogConnectionFactory
{
    Task<FakeConnection> OpenAsync();
}

public sealed class LogConnectionFactory : ILogConnectionFactory
{
    public Task<FakeConnection> OpenAsync() => Task.FromResult(new FakeConnection("log"));
}

/// <summary>테스트 픽스처라 실제 DB에 붙지 않는다. 분석기가 보는 것은 넘기는 SQL 문자열뿐이다.</summary>
public sealed class FakeConnection(string name) : IAsyncDisposable
{
    public string Name { get; } = name;

    public Task<int> ExecuteAsync(string sql, object? parameters = null) => Task.FromResult(0);

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null)
        where T : new() => Task.FromResult<IReadOnlyList<T>>([]);

    public Task<FakeTransaction> BeginTransactionAsync() => Task.FromResult(new FakeTransaction());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeTransaction : IAsyncDisposable
{
    public Task CommitAsync() => Task.CompletedTask;

    public Task RollbackAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class UserRow
{
    public int UserNo { get; set; }

    public string Nickname { get; set; } = string.Empty;
}

public sealed class MailRow
{
    public long MailId { get; set; }

    public int UserNo { get; set; }
}

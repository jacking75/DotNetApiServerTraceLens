namespace FixtureDataApp.Cache;

/// <summary>Redis 래퍼. 분석기는 타입 이름과 메서드 이름으로 명령(GET/SET/DEL/SET NX)을 정한다.</summary>
public interface IGameCache
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    Task SetAsync(string key, string value, TimeSpan expiry);

    Task RemoveAsync(string key);

    Task<bool> SetIfNotExistsAsync(string key, string value);

    Task<IReadOnlyList<string>> KeysAsync(string pattern);
}

public sealed class RedisGameCache : IGameCache
{
    public Task<string?> GetAsync(string key) => Task.FromResult<string?>(null);

    public Task SetAsync(string key, string value) => Task.CompletedTask;

    public Task SetAsync(string key, string value, TimeSpan expiry) => Task.CompletedTask;

    public Task RemoveAsync(string key) => Task.CompletedTask;

    public Task<bool> SetIfNotExistsAsync(string key, string value) => Task.FromResult(true);

    public Task<IReadOnlyList<string>> KeysAsync(string pattern) => Task.FromResult<IReadOnlyList<string>>([]);
}

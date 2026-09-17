// StackExchange.Redis 패키지를 넣지 않고 전체 이름만 맞춘 스텁이다.
// 픽스처 안에 있으므로 tracelens.config.json의 db.cacheWrappers로 "클라이언트 경계"라고 선언한다.
namespace StackExchange.Redis;

public interface IDatabase
{
    Task<string?> StringGetAsync(string key);

    Task<bool> StringSetAsync(string key, string value, TimeSpan? expiry = null);

    Task<bool> KeyDeleteAsync(string key);
}

public sealed class FakeDatabase : IDatabase
{
    public Task<string?> StringGetAsync(string key) => Task.FromResult<string?>(null);

    public Task<bool> StringSetAsync(string key, string value, TimeSpan? expiry = null) => Task.FromResult(true);

    public Task<bool> KeyDeleteAsync(string key) => Task.FromResult(true);
}

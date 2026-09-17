using StackExchange.Redis;

namespace FixtureDataApp.Cache;

/// <summary>
/// 솔루션 안 캐시 래퍼다. 래퍼를 부르는 자리가 아니라 <b>래퍼 본문</b>에서만 캐시 접근을 잡아야 한다.
/// 만료는 래퍼가 정하므로 호출 자리에서는 TTL 유무를 알 수 없다(CACHE-003 오탐의 원인이었다).
/// </summary>
public sealed class SessionCache(IDatabase database)
{
    public Task<bool> SetAsync(string key, string value) => database.StringSetAsync(key, value, TimeSpan.FromMinutes(5));

    /// <summary>만료 없이 쓰는 래퍼. CACHE-003은 이 줄에만 나야 한다.</summary>
    public Task<bool> SetForeverAsync(string key, string value) => database.StringSetAsync(key, value);
}

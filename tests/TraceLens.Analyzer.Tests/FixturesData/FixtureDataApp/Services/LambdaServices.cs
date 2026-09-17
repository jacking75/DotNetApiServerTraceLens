using FixtureDataApp.Cache;
using FixtureDataApp.Data;

namespace FixtureDataApp.Services;

/// <summary>
/// 트랜잭션 래퍼에 넘긴 람다 안에서 리포지터리를 부른다.
/// 람다 본문 호출이 그래프에 남는지(T1), 빌더 쿼리를 DB 접근으로 보는지(T2) 확인하는 픽스처다.
/// </summary>
public sealed class BuilderOrderService(
    GameDbBase db,
    BuilderOrderRepository orderRepository,
    LedgerRepository ledgerRepository)
{
    public Task<int> CreateAsync(int userNo, string itemId) =>
        db.TransactionAsync(async (query, transaction) =>
        {
            await orderRepository.InsertOrderAsync(query, transaction, userNo, itemId);
            await ledgerRepository.AppendAsync(query, transaction, userNo);
            return 1;
        });

    /// <summary>람다를 트랜잭션 래퍼가 아닌 곳에 넘긴다. 이 안의 호출은 트랜잭션 밖으로 봐야 한다.</summary>
    public Task NotifyLaterAsync(int userNo) => Task.Run(() => ledgerRepository.AppendFireAndForgetAsync(userNo));

    /// <summary>LINQ 람다 안의 호출도 그래프에 남아야 한다.</summary>
    public IReadOnlyList<string> Describe(IReadOnlyList<int> ids) => ids.Select(id => Format(id)).ToList();

    private static string Format(int id) => $"order-{id}";
}

/// <summary>
/// 캐시 래퍼를 부르는 쪽이다. 이 줄에는 캐시 접근도 CACHE-003도 나오면 안 된다 — 만료는 래퍼 안에서 정한다.
/// </summary>
public sealed class SessionService(SessionCache sessionCache)
{
    public Task<bool> SaveAsync(int userNo) => sessionCache.SetAsync($"session:{userNo}", "1");

    public Task<bool> SaveForeverAsync(int userNo) => sessionCache.SetForeverAsync($"session:{userNo}", "1");
}

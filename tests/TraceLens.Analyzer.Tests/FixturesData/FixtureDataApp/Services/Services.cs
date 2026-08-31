using FixtureDataApp.Cache;
using FixtureDataApp.Data;
using FixtureDataApp.Support;

namespace FixtureDataApp.Services;

/// <summary>
/// 트랜잭션 없이 4단계를 쓰고, 실패 경로에서는 1번만 되돌린다.
/// 2번(AddItemAsync)의 역연산(RemoveItemAsync)이 없어 COMP-002가 걸려야 한다.
/// </summary>
public sealed class CraftService(
    IShardConnectionFactory factory,
    ILogConnectionFactory logFactory,
    ItemRepository itemRepository,
    LogRepository logRepository,
    IGameCache cache)
{
    public async Task<bool> CraftAsync(int userNo, int itemId)
    {
        // TTL 없이 잡는 제작 락. CACHE-003 검증용이다.
        if (!await cache.SetIfNotExistsAsync($"lock:craft:{userNo}", "1"))
        {
            return false;
        }

        await using var connection = await factory.OpenAsync(userNo);
        try
        {
            await itemRepository.ConsumeMaterialsAsync(connection, userNo, itemId);
            await itemRepository.AddItemAsync(connection, userNo, itemId);
            await cache.RemoveAsync($"user:{userNo}:inv");
            await using var logConnection = await logFactory.OpenAsync();
            await logRepository.WriteCraftLogAsync(logConnection, userNo, itemId);
            return true;
        }
        catch (InvalidOperationException)
        {
            await itemRepository.RestoreMaterialsAsync(connection, userNo, itemId);
            return false;
        }
    }
}

/// <summary>
/// catch 경로에는 되돌리기가 있지만 <c>if (affected == 0) return false;</c> 경로에는 없다. COMP-006 검증용이다.
/// </summary>
public sealed class GuildService(IShardConnectionFactory factory, GuildRepository guildRepository, IGameCache cache)
{
    public async Task<bool> JoinAsync(int userNo, int guildId)
    {
        await using var connection = await factory.OpenAsync(userNo);
        try
        {
            await guildRepository.InsertMemberAsync(connection, guildId, userNo);
            var affected = await guildRepository.IncreaseMemberCountAsync(connection, guildId);
            if (affected == 0)
            {
                return false;
            }

            await cache.RemoveAsync($"guild:{guildId}:members");
            return true;
        }
        catch (InvalidOperationException)
        {
            await guildRepository.RemoveMemberAsync(connection, guildId, userNo);
            return false;
        }
    }
}

/// <summary>캐시를 먼저 갱신하고 DB 실패를 되돌리지 않는다. CACHE-002 검증용이다.</summary>
public sealed class AttendanceService(
    IShardConnectionFactory factory,
    AttendanceRepository attendanceRepository,
    CurrencyRepository currencyRepository,
    IGameCache cache)
{
    public async Task<bool> CheckAsync(int userNo, string day)
    {
        await using var connection = await factory.OpenAsync(userNo);
        try
        {
            await cache.SetAsync($"attendance:{userNo}:{day}", "1");
            await attendanceRepository.InsertAsync(connection, userNo, day);
            await currencyRepository.IncreaseAsync(connection, userNo, 100);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>쓰기 둘을 한 트랜잭션으로 묶고 캐시 무효화는 커밋 이후에 한다. 정상 사례다.</summary>
public sealed class ShopService(
    IShardConnectionFactory factory,
    ItemRepository itemRepository,
    CurrencyRepository currencyRepository,
    IGameCache cache)
{
    public async Task BuyAsync(int userNo, int itemId, int price)
    {
        await using var connection = await factory.OpenAsync(userNo);
        await using var transaction = await connection.BeginTransactionAsync();
        await currencyRepository.DecreaseAsync(connection, userNo, price);
        await itemRepository.AddItemAsync(connection, userNo, itemId);
        await transaction.CommitAsync();
        await cache.RemoveAsync($"user:{userNo}:inv");
    }

    /// <summary>비동기 호출을 동기로 기다린다. PERF-002 검증용이다.</summary>
    public int CountItems(int userNo)
    {
        var connection = factory.OpenAsync(userNo).Result;
        return connection.QueryAsync<UserRow>("SELECT user_no FROM user_item WHERE user_no = @user_no").Result.Count;
    }
}

/// <summary>되돌릴 수 없는 푸시 발송이 가운데에 있다. COMP-005 검증용이다.</summary>
public sealed class MailSendService(
    IShardConnectionFactory factory,
    MailRepository mailRepository,
    CurrencyRepository currencyRepository,
    IPushClient pushClient)
{
    public async Task<bool> SendAsync(int senderNo, int targetNo, string title, int cost)
    {
        await using var connection = await factory.OpenAsync(senderNo);
        try
        {
            await mailRepository.InsertAsync(connection, targetNo, title);
            await pushClient.SendAsync(targetNo, "새 우편");
            await currencyRepository.DecreaseAsync(connection, senderNo, cost);
            return true;
        }
        catch (InvalidOperationException)
        {
            await mailRepository.DeleteAsync(connection, targetNo, title);
            return false;
        }
    }
}

/// <summary>트랜잭션 안에서 다른 DB 커넥션·외부 호출·캐시를 건드린다. TX-003 · TX-001 · CACHE-007 검증용이다.</summary>
public sealed class MailReceiveService(
    IShardConnectionFactory factory,
    ILogConnectionFactory logFactory,
    MailRepository mailRepository,
    LogRepository logRepository,
    IBillingClient billingClient,
    IGameCache cache)
{
    public async Task ReceiveAllAsync(int userNo)
    {
        await using var connection = await factory.OpenAsync(userNo);
        await using var transaction = await connection.BeginTransactionAsync();
        await mailRepository.LoadReceivableAsync(connection, userNo);
        await billingClient.GrantAsync(userNo, 100);
        await mailRepository.MarkReceivedAsync(connection, userNo);
        await using var logConnection = await logFactory.OpenAsync();
        await logRepository.WriteCraftLogAsync(logConnection, userNo, 0);
        await cache.RemoveAsync($"user:{userNo}:mail");
        await transaction.CommitAsync();
    }
}

/// <summary>내 샤드 커넥션으로 친구를 조회하고, 반복문 안에서 쿼리와 캐시를 하나씩 부른다.</summary>
public sealed class FriendService(IShardConnectionFactory factory, ItemRepository itemRepository, IGameCache cache)
{
    public async Task<int> GetFriendProfilesAsync(int userNo, IReadOnlyList<int> friendNos)
    {
        await using var connection = await factory.OpenAsync(userNo);
        var count = 0;
        foreach (var friendNo in friendNos)
        {
            await cache.GetAsync($"user:{friendNo}:profile");
            var rows = await itemRepository.GetProfileAsync(connection, friendNo);
            count += rows.Count;
        }

        return count;
    }
}

/// <summary>샤드를 순회하며 집계하고, 읽기 전용 복제본에 스냅샷을 쓴다.</summary>
public sealed class RankingService(
    IShardConnectionFactory factory,
    IReplicaConnectionFactory replicaFactory,
    RankingRepository rankingRepository)
{
    public async Task<int> BuildTopAsync()
    {
        var total = 0;
        for (var shard = 0; shard < 8; shard++)
        {
            await using var connection = await factory.OpenAsync(shard);
            var rows = await rankingRepository.LoadTopAsync(connection, 100);
            total += rows.Count;
        }

        await rankingRepository.UpdateSnapshotAsync(replicaFactory, 1, total);
        return total;
    }
}

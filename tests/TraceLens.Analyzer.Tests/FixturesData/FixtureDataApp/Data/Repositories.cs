namespace FixtureDataApp.Data;

/// <summary>아이템 테이블. 되돌리기 짝(Consume↔Restore, Add↔Remove)이 이름 규칙으로 잡히는지 확인하는 데 쓴다.</summary>
public sealed class ItemRepository
{
    public Task ConsumeMaterialsAsync(FakeConnection connection, int userNo, int itemId) =>
        connection.ExecuteAsync(
            "UPDATE user_item SET count = count - @count WHERE user_no = @user_no AND item_id = @item_id",
            new { userNo, itemId });

    public Task RestoreMaterialsAsync(FakeConnection connection, int userNo, int itemId) =>
        connection.ExecuteAsync(
            "UPDATE user_item SET count = count + @count WHERE user_no = @user_no AND item_id = @item_id",
            new { userNo, itemId });

    public Task AddItemAsync(FakeConnection connection, int userNo, int itemId) =>
        connection.ExecuteAsync(
            "INSERT INTO user_item (user_no, item_id, count) VALUES (@user_no, @item_id, 1)",
            new { userNo, itemId });

    /// <summary>보상용 역연산. CraftAsync의 실패 경로에서는 일부러 부르지 않는다.</summary>
    public Task RemoveItemAsync(FakeConnection connection, int userNo, int itemId) =>
        connection.ExecuteAsync(
            "DELETE FROM user_item WHERE user_no = @user_no AND item_id = @item_id",
            new { userNo, itemId });

    public Task<IReadOnlyList<UserRow>> GetProfileAsync(FakeConnection connection, int userNo) =>
        connection.QueryAsync<UserRow>(
            "SELECT user_no, nickname FROM user_profile WHERE user_no = @user_no",
            new { userNo });
}

public sealed class CurrencyRepository
{
    public Task IncreaseAsync(FakeConnection connection, int userNo, int amount) =>
        connection.ExecuteAsync(
            "UPDATE user_currency SET gold = gold + @gold WHERE user_no = @user_no",
            new { userNo, amount });

    public Task DecreaseAsync(FakeConnection connection, int userNo, int amount) =>
        connection.ExecuteAsync(
            "UPDATE user_currency SET gold = gold - @gold WHERE user_no = @user_no",
            new { userNo, amount });
}

public sealed class GuildRepository
{
    public Task InsertMemberAsync(FakeConnection connection, int guildId, int userNo) =>
        connection.ExecuteAsync(
            "INSERT INTO guild_member (guild_id, user_no) VALUES (@guild_id, @user_no)",
            new { guildId, userNo });

    public Task<int> IncreaseMemberCountAsync(FakeConnection connection, int guildId) =>
        connection.ExecuteAsync(
            "UPDATE guild SET member_count = member_count + 1 WHERE guild_id = @guild_id",
            new { guildId });

    public Task RemoveMemberAsync(FakeConnection connection, int guildId, int userNo) =>
        connection.ExecuteAsync(
            "DELETE FROM guild_member WHERE guild_id = @guild_id AND user_no = @user_no",
            new { guildId, userNo });
}

public sealed class AttendanceRepository
{
    public Task InsertAsync(FakeConnection connection, int userNo, string day) =>
        connection.ExecuteAsync(
            "INSERT INTO attendance_log (user_no, day) VALUES (@user_no, @day)",
            new { userNo, day });
}

public sealed class MailRepository
{
    public Task InsertAsync(FakeConnection connection, int targetNo, string title) =>
        connection.ExecuteAsync(
            "INSERT INTO user_mail (user_no, title, state) VALUES (@user_no, @title, 0)",
            new { targetNo, title });

    public Task DeleteAsync(FakeConnection connection, int targetNo, string title) =>
        connection.ExecuteAsync(
            "DELETE FROM user_mail WHERE user_no = @user_no AND title = @title",
            new { targetNo, title });

    public Task<IReadOnlyList<MailRow>> LoadReceivableAsync(FakeConnection connection, int userNo) =>
        connection.QueryAsync<MailRow>(
            "SELECT mail_id, user_no FROM user_mail WHERE user_no = @user_no AND state = 0 AND expire_at > NOW()",
            new { userNo });

    public Task MarkReceivedAsync(FakeConnection connection, int userNo) =>
        connection.ExecuteAsync(
            "UPDATE user_mail SET state = 1 WHERE user_no = @user_no AND state = 0",
            new { userNo });
}

public sealed class LogRepository
{
    public Task WriteCraftLogAsync(FakeConnection connection, int userNo, int itemId) =>
        connection.ExecuteAsync(
            "INSERT INTO item_log (user_no, item_id, at) VALUES (@user_no, @item_id, NOW())",
            new { userNo, itemId });
}

public sealed class RankingRepository
{
    public Task<IReadOnlyList<UserRow>> LoadTopAsync(FakeConnection connection, int limit) =>
        connection.QueryAsync<UserRow>(
            "SELECT user_no, nickname FROM user_profile ORDER BY score DESC LIMIT 100",
            new { limit });

    /// <summary>읽기 전용 복제본에 쓰기를 한다. RW-001 검증용이다.</summary>
    public async Task UpdateSnapshotAsync(IReplicaConnectionFactory factory, int userNo, int rank)
    {
        await using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE ranking_snapshot SET rank = @rank WHERE user_no = @user_no",
            new { userNo, rank });
    }
}

public sealed class NoticeRepository
{
    /// <summary>문자열 보간으로 SQL을 만든다. SEC-001 검증용이다.</summary>
    public Task<IReadOnlyList<UserRow>> SearchAsync(FakeConnection connection, string keyword) =>
        connection.QueryAsync<UserRow>($"SELECT * FROM notice WHERE title LIKE '%{keyword}%' ORDER BY id DESC");
}

/// <summary>using 없이 커넥션을 연다. CONN-001 검증용이다.</summary>
public sealed class LegacyStatRepository(IShardConnectionFactory factory)
{
    public async Task<IReadOnlyList<UserRow>> LoadAllAsync(int userNo)
    {
        var connection = await factory.OpenAsync(userNo);
        return await connection.QueryAsync<UserRow>("SELECT user_no, nickname FROM user_profile");
    }
}

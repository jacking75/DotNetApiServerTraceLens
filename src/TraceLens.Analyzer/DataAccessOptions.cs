using System.Text.Json;

namespace TraceLens.Analyzer;

/// <summary>
/// DB·캐시 보기가 "무엇이 쿼리이고 무엇이 되돌리기인지"를 알기 위해 받는 설정(<c>tracelens.config.json</c>의 <c>db</c> 섹션).
/// 하나도 없어도 동작한다 — 그때는 SQL 리터럴과 타입 이름에서 짐작한다.
/// </summary>
/// <param name="QueryApis">쿼리 실행 API 패턴(<c>Dapper.SqlMapper.*</c> 등). 전체 이름에 <c>*</c> 와일드카드를 쓴다.</param>
/// <param name="CacheApis">캐시 클라이언트 API 패턴.</param>
/// <param name="ConnectionFactories">커넥션을 여는 메서드 패턴. 첫 인자를 샤드 키로 본다.</param>
/// <param name="ShardKeyParameters">샤드 키로 쓰이는 파라미터 이름(<c>userNo</c> 등).</param>
/// <param name="ExternalApis">상태를 바꾸는 외부 호출 패턴(HTTP 클라이언트 등). 트랜잭션 안 외부 호출 판정에 쓴다.</param>
/// <param name="TransactionWrappers">
/// 람다를 받아 그 안에서 트랜잭션을 여닫는 래퍼 메서드 패턴. 래퍼에 넘긴 람다 안의 쓰기는 트랜잭션 안으로 본다.
/// 패턴으로 못 잡아도 "본문에 BeginTransaction이 있고 대리자 파라미터를 부르는 메서드"는 자동으로 래퍼로 본다.
/// </param>
/// <param name="CacheWrappers">
/// 솔루션 안에 있어도 캐시 클라이언트로 취급할 래퍼 패턴. 기본은 솔루션 안 메서드를 캐시 API로 보지 않는다.
/// </param>
public sealed record DataAccessOptions(
    IReadOnlyList<string> QueryApis,
    IReadOnlyList<string> CacheApis,
    IReadOnlyList<string> ConnectionFactories,
    IReadOnlyList<string> ShardKeyParameters,
    IReadOnlyList<string> ExternalApis,
    IReadOnlyList<DataSourceOption> DataSources,
    CompensationOptions Compensation,
    IReadOnlyList<string> TransactionWrappers,
    IReadOnlyList<string> CacheWrappers,
    IReadOnlyList<QueryBuilderOption> QueryBuilders)
{
    public static DataAccessOptions Default { get; } = new(
        ["Dapper.SqlMapper.*", "*.IDbConnection.*", "*DbConnection.Execute*", "*DbConnection.Query*"],
        ["StackExchange.Redis.IDatabase.*", "*.IDistributedCache.*", "CloudStructures.*", "*Cache.*", "*Redis*.*"],
        ["*ConnectionFactory.Open*", "*ConnectionFactory.Create*", "*.OpenConnection*"],
        ["userNo", "userId", "uid", "playerNo", "memberNo", "accountId"],
        ["System.Net.Http.HttpClient.*", "*PushClient.*", "*MailSender.*", "*BillingClient.*"],
        [],
        CompensationOptions.Default,
        ["*.TransactionAsync", "*.InTransactionAsync", "*.ExecuteInTransaction*", "*.RunInTransaction*", "*.WithTransaction*"],
        [],
        QueryBuilderOption.Defaults);

    /// <summary>설정에 샤드로 표시한 데이터소스가 하나라도 있으면 SHARD 규칙군을 켠다.</summary>
    public bool ShardingEnabled => DataSources.Any(item =>
        string.Equals(item.Kind, "shard", StringComparison.OrdinalIgnoreCase));

    public static DataAccessOptions Read(JsonElement root)
    {
        if (!root.TryGetProperty("db", out var db) || db.ValueKind != JsonValueKind.Object)
        {
            return Default;
        }

        return new DataAccessOptions(
            ReadStrings(db, "queryApis") ?? Default.QueryApis,
            ReadStrings(db, "cacheApis") ?? Default.CacheApis,
            ReadStrings(db, "connectionFactories") ?? Default.ConnectionFactories,
            ReadStrings(db, "shardKeyParameters") ?? Default.ShardKeyParameters,
            ReadStrings(db, "externalApis") ?? Default.ExternalApis,
            ReadDataSources(db),
            CompensationOptions.Read(db),
            ReadStrings(db, "transactionWrappers") ?? Default.TransactionWrappers,
            ReadStrings(db, "cacheWrappers") ?? Default.CacheWrappers,
            QueryBuilderOption.Read(db) ?? Default.QueryBuilders);
    }

    private static IReadOnlyList<DataSourceOption> ReadDataSources(JsonElement db)
    {
        if (!db.TryGetProperty("dataSources", out var sources) || sources.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return sources.EnumerateObject().Select(property =>
        {
            var value = property.Value;
            return new DataSourceOption(
                property.Name,
                value.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "single" : "single",
                value.TryGetProperty("shardCount", out var shardCount) ? shardCount.GetInt32() : 0,
                value.TryGetProperty("readOnly", out var readOnly) && readOnly.GetBoolean(),
                value.TryGetProperty("writeOnly", out var writeOnly) && writeOnly.GetBoolean(),
                ReadStrings(value, "typePatterns") ?? []);
        }).ToArray();
    }

    internal static IReadOnlyList<string>? ReadStrings(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : null;
}

/// <param name="TypePatterns">이 데이터소스로 볼 타입 이름 패턴(<c>*ShardConnectionFactory</c> 등).</param>
public sealed record DataSourceOption(
    string Name,
    string Kind,
    int ShardCount,
    bool ReadOnly,
    bool WriteOnly,
    IReadOnlyList<string> TypePatterns);

/// <param name="Pairs">되돌리기 짝. 이름 규칙으로 못 잡는 사내 관용 형태를 여기에 적는다.</param>
/// <param name="Irreversible">취소 수단이 없는 호출 패턴.</param>
/// <param name="Idempotent">재실행해도 안전해서 되돌릴 필요가 없는 호출 패턴.</param>
public sealed record CompensationOptions(
    IReadOnlyList<UndoPair> Pairs,
    IReadOnlyList<string> Irreversible,
    IReadOnlyList<string> Idempotent)
{
    public static CompensationOptions Default { get; } = new([], [], []);

    /// <summary>이름만으로 짐작하는 되돌리기 짝. 설정에 적은 짝이 먼저다.</summary>
    public static IReadOnlyList<UndoPair> NameHeuristics { get; } =
    [
        new("Insert", "Delete"),
        new("Add", "Remove"),
        new("Create", "Delete"),
        new("Increase", "Decrease"),
        new("Consume", "Restore"),
        new("Deduct", "Restore"),
        new("Set", "Remove"),
        new("Grant", "Revoke"),
        new("Reserve", "Release"),
        new("Acquire", "Release")
    ];

    public static CompensationOptions Read(JsonElement db)
    {
        if (!db.TryGetProperty("compensation", out var compensation) || compensation.ValueKind != JsonValueKind.Object)
        {
            return Default;
        }

        var pairs = compensation.TryGetProperty("pairs", out var pairsValue) && pairsValue.ValueKind == JsonValueKind.Array
            ? pairsValue.EnumerateArray()
                .Select(item => new UndoPair(
                    item.TryGetProperty("do", out var doValue) ? doValue.GetString() ?? string.Empty : string.Empty,
                    item.TryGetProperty("undo", out var undoValue) ? undoValue.GetString() ?? string.Empty : string.Empty))
                .Where(pair => pair.Do.Length > 0 && pair.Undo.Length > 0)
                .ToArray()
            : [];

        return new CompensationOptions(
            pairs,
            DataAccessOptions.ReadStrings(compensation, "irreversible") ?? [],
            DataAccessOptions.ReadStrings(compensation, "idempotent") ?? []);
    }
}

public sealed record UndoPair(string Do, string Undo);

internal static class PatternMatch
{
    /// <summary><c>*</c> 와일드카드만 지원하는 대소문자 무시 매칭. 설정 패턴이 짧아 정규식까지 갈 필요가 없다.</summary>
    public static bool Matches(string value, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        var segments = pattern.Split('*');
        var index = 0;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0)
            {
                continue;
            }

            if (i == 0)
            {
                if (!value.StartsWith(segment, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                index = segment.Length;
                continue;
            }

            var found = value.IndexOf(segment, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            index = found + segment.Length;
        }

        return segments[^1].Length == 0 || value.EndsWith(segments[^1], StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesAny(string value, IReadOnlyList<string> patterns) =>
        patterns.Any(pattern => Matches(value, pattern));
}

using System.Text.Json;

namespace TraceLens.Analyzer;

/// <summary>
/// SQL 리터럴 없이 빌더 체인으로 쿼리하는 라이브러리(SqlKata 등) 하나에 대한 설정이다.
/// <c>db.Query("table").Where(…).UpdateAsync(…)</c>처럼 <see cref="Root"/>에서 테이블을 받고
/// <see cref="Terminals"/>에서 실제로 실행하는 형태를 DB 접근으로 인식한다.
/// </summary>
/// <param name="Root">테이블 이름을 받는 시작 메서드의 전체 이름(<c>SqlKata.Execution.QueryFactory.Query</c>).</param>
/// <param name="TableArgumentIndex"><see cref="Root"/> 호출에서 테이블 이름이 몇 번째 인자인지.</param>
/// <param name="QueryType">체인 중간 타입의 전체 이름(<c>SqlKata.Query</c>). 이 타입(또는 파생)이어야 후보로 본다.</param>
/// <param name="Terminals">실제로 실행하는 메서드 이름 → 연산(Read·Write·Delete).</param>
/// <param name="JoinMethods">조인 대상 테이블을 읽기 대상으로 추가할 메서드 이름.</param>
public sealed record QueryBuilderOption(
    string Root,
    int TableArgumentIndex,
    string QueryType,
    IReadOnlyDictionary<string, AccessOperationName> Terminals,
    IReadOnlyList<string> JoinMethods)
{
    /// <summary>설정이 없어도 SqlKata는 인식한다. <c>db.queryBuilders</c>를 적으면 이 기본값을 대체한다.</summary>
    public static IReadOnlyList<QueryBuilderOption> Defaults { get; } =
    [
        new QueryBuilderOption(
            "SqlKata.Execution.QueryFactory.Query",
            0,
            "SqlKata.Query",
            new Dictionary<string, AccessOperationName>(StringComparer.OrdinalIgnoreCase)
            {
                ["GetAsync"] = AccessOperationName.Read,
                ["Get"] = AccessOperationName.Read,
                ["FirstOrDefaultAsync"] = AccessOperationName.Read,
                ["FirstOrDefault"] = AccessOperationName.Read,
                ["FirstAsync"] = AccessOperationName.Read,
                ["First"] = AccessOperationName.Read,
                ["CountAsync"] = AccessOperationName.Read,
                ["Count"] = AccessOperationName.Read,
                ["ExistsAsync"] = AccessOperationName.Read,
                ["PaginateAsync"] = AccessOperationName.Read,
                ["InsertAsync"] = AccessOperationName.Write,
                ["Insert"] = AccessOperationName.Write,
                ["InsertGetIdAsync"] = AccessOperationName.Write,
                ["UpdateAsync"] = AccessOperationName.Write,
                ["Update"] = AccessOperationName.Write,
                ["DeleteAsync"] = AccessOperationName.Delete,
                ["Delete"] = AccessOperationName.Delete,
                ["IncrementAsync"] = AccessOperationName.Write,
                ["DecrementAsync"] = AccessOperationName.Write
            },
            ["Join", "LeftJoin", "RightJoin"])
    ];

    public static IReadOnlyList<QueryBuilderOption>? Read(JsonElement db)
    {
        if (!db.TryGetProperty("queryBuilders", out var builders) || builders.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<QueryBuilderOption>();
        foreach (var item in builders.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var root = item.TryGetProperty("root", out var rootValue) ? rootValue.GetString() ?? string.Empty : string.Empty;
            var queryType = item.TryGetProperty("queryType", out var typeValue)
                ? typeValue.GetString() ?? string.Empty
                : string.Empty;
            if (root.Length == 0 || queryType.Length == 0)
            {
                continue;
            }

            var terminals = new Dictionary<string, AccessOperationName>(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetProperty("terminals", out var terminalsValue) && terminalsValue.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in terminalsValue.EnumerateObject())
                {
                    terminals[property.Name] = Enum.TryParse<AccessOperationName>(property.Value.GetString(), true, out var parsed)
                        ? parsed
                        : AccessOperationName.Other;
                }
            }

            result.Add(new QueryBuilderOption(
                root,
                item.TryGetProperty("tableArgumentIndex", out var indexValue) && indexValue.ValueKind == JsonValueKind.Number
                    ? indexValue.GetInt32()
                    : 0,
                queryType,
                terminals,
                DataAccessOptions.ReadStrings(item, "joinMethods") ?? []));
        }

        return result;
    }
}

/// <summary>설정 JSON에 적는 연산 이름. 모델의 <c>AccessOperation</c>과 같은 값이지만 설정 파싱에서만 쓴다.</summary>
public enum AccessOperationName
{
    Read,
    Write,
    Delete,
    Other
}

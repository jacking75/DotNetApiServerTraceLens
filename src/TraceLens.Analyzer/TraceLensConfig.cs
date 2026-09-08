using System.Text.Json;

namespace TraceLens.Analyzer;

public enum AnalysisScope
{
    /// <summary>솔루션 안의 모든 메서드를 훑어 호출 그래프를 만든다(기본값).</summary>
    Solution,

    /// <summary>진입점에서 도달 가능한 메서드만 훑는다. 클래스 목록은 이 모드에서도 솔루션 전체를 담는다.</summary>
    EntryPoints
}

/// <param name="IncludeObjectCreation">
/// <c>new X()</c>로 만드는 솔루션 타입도 생성자 노드로 그래프에 넣을지 여부. DTO가 많은 코드베이스에서 그래프를 줄이려면 끈다.
/// </param>
/// <param name="IncludeMembers">
/// 클래스 다이어그램용 멤버(필드·프로퍼티·메서드·생성자)를 수집할지 여부. 끄면 <c>graph.json</c>이 작아지는 대신 UML 박스가 빈 채로 그려진다.
/// </param>
/// <param name="IncludeInterfaces">
/// 인터페이스 선언도 <c>classes</c>에 넣을지 여부. 끄면 스키마 v3까지의 동작(인터페이스 제외)으로 돌아간다.
/// </param>
/// <param name="IncludeStartupFlow">
/// Program.cs를 훑어 DI 등록·미들웨어 파이프라인("시작 흐름")을 수집할지 여부. 끄면 그 보기가 빈 상태로 남는다.
/// </param>
/// <param name="IncludeDataAccess">
/// DB·캐시 접근과 보상 처리(트랜잭션 없는 쓰기 경로)를 수집할지 여부. 끄면 "DB · 캐시" 보기가 빈 상태로 남는다.
/// </param>
/// <param name="IncludeMiddleware">
/// 미들웨어 파이프라인을 열어 요청/응답 구간·단락을 수집할지 여부. 끄면 "미들웨어" 보기가 빈 상태로 남는다.
/// </param>
/// <param name="IncludeCallSites">
/// 호출 엣지의 근거(실제 호출 줄·해석 종류)를 <c>graph.json</c>에 남길지 여부. 끄면 파일이 작아지는 대신
/// UI·AI 문맥에서 "왜 이 엣지가 생겼는지"를 보여줄 수 없다.
/// </param>
/// <param name="MaxCallSitesPerEdge">한 엣지에 남길 근거 수 상한. 반복문 안 호출로 파일이 커지는 것을 막는다.</param>
/// <param name="MaxBoundaryCallSites">미해석·모호 호출(경계 목록)의 상한.</param>
/// <param name="Db">
/// 사내 DB·캐시 계층의 모양을 알려주는 설정. 비워 두면 SQL 리터럴과 타입 이름에서 짐작한다.
/// </param>
public sealed record TraceLensConfig(
    IReadOnlyList<string> EntryPointAttributes,
    IReadOnlyList<KindRule> KindRules,
    int MaxCallDepth,
    IReadOnlyList<string> ExcludeNamespacePrefixes,
    AnalysisScope AnalysisScope,
    bool IncludeObjectCreation,
    bool IncludeMembers,
    bool IncludeInterfaces,
    bool IncludeStartupFlow,
    bool IncludeDataAccess,
    bool IncludeMiddleware,
    bool IncludeCallSites,
    int MaxCallSitesPerEdge,
    int MaxBoundaryCallSites,
    DataAccessOptions Db)
{
    public static TraceLensConfig Default { get; } = new(
        ["HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch"],
        [
            new("Controller", "ControllerBase", []),
            new("Repository", null, ["Repository"]),
            new("Validator", null, ["Validator"]),
            new("Infra", null, ["Publisher", "Client", "Bus"]),
            new("Service", null, ["Service"]),
            new("Db", "DbContext", [])
        ],
        8,
        ["System", "Microsoft", "Swashbuckle"],
        AnalysisScope.Solution,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        3,
        300,
        DataAccessOptions.Default);

    public static async Task<TraceLensConfig> LoadAsync(
        string solutionDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(solutionDirectory, "tracelens.config.json");
        if (!File.Exists(path))
        {
            return Default;
        }

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        return new TraceLensConfig(
            ReadStringArray(root, "entryPointAttributes") ?? Default.EntryPointAttributes,
            ReadKindRules(root) ?? Default.KindRules,
            root.TryGetProperty("maxCallDepth", out var depth) ? depth.GetInt32() : Default.MaxCallDepth,
            ReadStringArray(root, "excludeNamespacePrefixes") ?? Default.ExcludeNamespacePrefixes,
            ReadAnalysisScope(root) ?? Default.AnalysisScope,
            root.TryGetProperty("includeObjectCreation", out var includeObjectCreation)
                ? includeObjectCreation.GetBoolean()
                : Default.IncludeObjectCreation,
            root.TryGetProperty("includeMembers", out var includeMembers)
                ? includeMembers.GetBoolean()
                : Default.IncludeMembers,
            root.TryGetProperty("includeInterfaces", out var includeInterfaces)
                ? includeInterfaces.GetBoolean()
                : Default.IncludeInterfaces,
            root.TryGetProperty("includeStartupFlow", out var includeStartupFlow)
                ? includeStartupFlow.GetBoolean()
                : Default.IncludeStartupFlow,
            root.TryGetProperty("includeDataAccess", out var includeDataAccess)
                ? includeDataAccess.GetBoolean()
                : Default.IncludeDataAccess,
            root.TryGetProperty("includeMiddleware", out var includeMiddleware)
                ? includeMiddleware.GetBoolean()
                : Default.IncludeMiddleware,
            root.TryGetProperty("includeCallSites", out var includeCallSites)
                ? includeCallSites.GetBoolean()
                : Default.IncludeCallSites,
            root.TryGetProperty("maxCallSitesPerEdge", out var maxCallSitesPerEdge)
                ? maxCallSitesPerEdge.GetInt32()
                : Default.MaxCallSitesPerEdge,
            root.TryGetProperty("maxBoundaryCallSites", out var maxBoundaryCallSites)
                ? maxBoundaryCallSites.GetInt32()
                : Default.MaxBoundaryCallSites,
            DataAccessOptions.Read(root));
    }

    private static AnalysisScope? ReadAnalysisScope(JsonElement root) =>
        root.TryGetProperty("analysisScope", out var scope) &&
        Enum.TryParse<AnalysisScope>(scope.GetString(), true, out var parsed)
            ? parsed
            : null;

    private static IReadOnlyList<KindRule>? ReadKindRules(JsonElement root)
    {
        if (!root.TryGetProperty("kindRules", out var rules))
        {
            return null;
        }

        return rules.EnumerateArray().Select(rule => new KindRule(
            rule.GetProperty("kind").GetString() ?? "Method",
            rule.TryGetProperty("matchBaseType", out var baseType) ? baseType.GetString() : null,
            rule.TryGetProperty("matchNameSuffix", out var suffix)
                ? suffix.ValueKind == JsonValueKind.Array
                    ? suffix.EnumerateArray().Select(value => value.GetString()!).ToArray()
                    : [suffix.GetString()!]
                : [])).ToArray();
    }

    private static IReadOnlyList<string>? ReadStringArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value)
            ? value.EnumerateArray().Select(item => item.GetString()!).ToArray()
            : null;
}

public sealed record KindRule(string Kind, string? MatchBaseType, IReadOnlyList<string> MatchNameSuffix);

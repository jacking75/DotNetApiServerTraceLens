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

    /// <summary>설정을 읽는다. 문제가 있어도 예외로 분석을 죽이지 않고 기본값으로 대체한 뒤 사유를 함께 돌려준다.</summary>
    public static async Task<ConfigLoadResult> LoadAsync(
        string solutionDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(solutionDirectory, "tracelens.config.json");
        if (!File.Exists(path))
        {
            return new ConfigLoadResult(Default, []);
        }

        JsonDocument document;
        try
        {
            await using var stream = File.OpenRead(path);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            // 설정이 깨졌다고 분석 전체를 죽이지 않는다. 기본 설정으로 진행하고 그 사실을 남긴다.
            return new ConfigLoadResult(
                Default,
                [new ConfigIssue(ConfigIssueSeverity.Error, $"tracelens.config.json을 읽지 못해 기본 설정으로 분석한다: {exception.Message}")]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ConfigLoadResult(
                Default,
                [new ConfigIssue(ConfigIssueSeverity.Error, $"tracelens.config.json을 열지 못해 기본 설정으로 분석한다: {exception.Message}")]);
        }

        using (document)
        {
            return Read(document.RootElement);
        }
    }

    /// <summary>설정 JSON 하나를 읽는다. 타입이 맞지 않으면 기본값으로 대체하고 경고를 남긴다.</summary>
    public static ConfigLoadResult Read(JsonElement root)
    {
        var issues = new List<ConfigIssue>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ConfigLoadResult(
                Default,
                [new ConfigIssue(ConfigIssueSeverity.Error, "tracelens.config.json의 최상위가 객체가 아니다. 기본 설정으로 분석한다.")]);
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!KnownKeys.Contains(property.Name))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Warning,
                    $"tracelens.config.json에 모르는 키가 있다: {property.Name}. 이 값은 쓰지 않는다."));
            }
        }

        var config = new TraceLensConfig(
            ReadStringArray(root, "entryPointAttributes") ?? Default.EntryPointAttributes,
            ReadKindRules(root) ?? Default.KindRules,
            ReadInt("maxCallDepth", Default.MaxCallDepth),
            ReadStringArray(root, "excludeNamespacePrefixes") ?? Default.ExcludeNamespacePrefixes,
            ReadAnalysisScope(root) ?? Default.AnalysisScope,
            ReadBool("includeObjectCreation", Default.IncludeObjectCreation),
            ReadBool("includeMembers", Default.IncludeMembers),
            ReadBool("includeInterfaces", Default.IncludeInterfaces),
            ReadBool("includeStartupFlow", Default.IncludeStartupFlow),
            ReadBool("includeDataAccess", Default.IncludeDataAccess),
            ReadBool("includeMiddleware", Default.IncludeMiddleware),
            ReadBool("includeCallSites", Default.IncludeCallSites),
            ReadInt("maxCallSitesPerEdge", Default.MaxCallSitesPerEdge),
            ReadInt("maxBoundaryCallSites", Default.MaxBoundaryCallSites),
            DataAccessOptions.Read(root));
        return new ConfigLoadResult(config, issues);

        int ReadInt(string name, int fallback)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                return fallback;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
            {
                return parsed;
            }

            issues.Add(new ConfigIssue(
                ConfigIssueSeverity.Warning, $"{name}은(는) 숫자여야 한다. 기본값 {fallback}을(를) 쓴다."));
            return fallback;
        }

        bool ReadBool(string name, bool fallback)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                return fallback;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            issues.Add(new ConfigIssue(
                ConfigIssueSeverity.Warning, $"{name}은(는) true/false여야 한다. 기본값 {fallback}을(를) 쓴다."));
            return fallback;
        }
    }

    /// <summary>알고 있는 최상위 키. 여기에 없는 키는 경고로 알린다(조용히 무시하지 않는다).</summary>
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "entryPointAttributes", "kindRules", "maxCallDepth", "excludeNamespacePrefixes", "analysisScope",
        "includeObjectCreation", "includeMembers", "includeInterfaces", "includeStartupFlow",
        "includeDataAccess", "includeMiddleware", "includeCallSites",
        "maxCallSitesPerEdge", "maxBoundaryCallSites", "db",
        // JSON에는 주석이 없어서 관용적으로 쓰는 키다. 경고하지 않는다.
        "_comment", "$schema"
    };

    private static AnalysisScope? ReadAnalysisScope(JsonElement root) =>
        root.TryGetProperty("analysisScope", out var scope) &&
        Enum.TryParse<AnalysisScope>(scope.GetString(), true, out var parsed)
            ? parsed
            : null;

    private static IReadOnlyList<KindRule>? ReadKindRules(JsonElement root)
    {
        if (!root.TryGetProperty("kindRules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return rules.EnumerateArray()
            .Where(rule => rule.ValueKind == JsonValueKind.Object && rule.TryGetProperty("kind", out _))
            .Select(rule => new KindRule(
            rule.GetProperty("kind").GetString() ?? "Method",
            rule.TryGetProperty("matchBaseType", out var baseType) ? baseType.GetString() : null,
            rule.TryGetProperty("matchNameSuffix", out var suffix)
                ? suffix.ValueKind == JsonValueKind.Array
                    ? suffix.EnumerateArray().Select(value => value.GetString()!).ToArray()
                    : [suffix.GetString()!]
                : [])).ToArray();
    }

    private static IReadOnlyList<string>? ReadStringArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .Where(item => item is not null)
                .Select(item => item!)
                .ToArray()
            : null;
}

public sealed record KindRule(string Kind, string? MatchBaseType, IReadOnlyList<string> MatchNameSuffix);

/// <summary>설정을 읽으며 알아낸 문제 하나. 분석을 멈추지 않고 진단과 화면에 남긴다.</summary>
public enum ConfigIssueSeverity
{
    Warning,
    Error
}

public sealed record ConfigIssue(ConfigIssueSeverity Severity, string Message);

/// <param name="Issues">설정을 읽으며 알아낸 문제. 비어 있으면 그대로 읽힌 것이다.</param>
public sealed record ConfigLoadResult(TraceLensConfig Config, IReadOnlyList<ConfigIssue> Issues);

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
public sealed record TraceLensConfig(
    IReadOnlyList<string> EntryPointAttributes,
    IReadOnlyList<KindRule> KindRules,
    int MaxCallDepth,
    IReadOnlyList<string> ExcludeNamespacePrefixes,
    AnalysisScope AnalysisScope,
    bool IncludeObjectCreation)
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
        true);

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
                : Default.IncludeObjectCreation);
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

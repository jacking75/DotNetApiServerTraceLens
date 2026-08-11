using System.Text.Json;

namespace TraceLens.Analyzer;

public sealed record TraceLensConfig(
    IReadOnlyList<string> EntryPointAttributes,
    IReadOnlyList<KindRule> KindRules,
    int MaxCallDepth,
    IReadOnlyList<string> ExcludeNamespacePrefixes)
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
        ["System", "Microsoft", "Swashbuckle"]);

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
            ReadStringArray(root, "excludeNamespacePrefixes") ?? Default.ExcludeNamespacePrefixes);
    }

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

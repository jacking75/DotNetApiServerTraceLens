using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// 스키마와 코드가 갈라지면 자동완성이 조용히 틀린다.
/// 스키마에만 있는 키는 "쓰면 되는 줄 알았는데 무시되는" 키이고,
/// 코드에만 있는 키는 편집기가 빨간 줄을 긋는 키다. 둘 다 사용자가 손해를 본다.
/// </summary>
public sealed class ConfigSchemaTests
{
    [Fact]
    public void Schema_TopLevelPropertiesMatchKnownKeys()
    {
        var schema = ReadSchema();
        // 주석 대용 키(_comment…)는 스키마의 patternProperties가 받고 코드는 접두사로 허용한다. 비교에서 뺀다.
        var schemaKeys = schema.RootElement.GetProperty("properties").EnumerateObject()
            .Select(property => property.Name)
            .Where(name => !name.StartsWith("_comment", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var codeKeys = TraceLensConfig.TopLevelKeys
            .Where(name => !name.StartsWith("_comment", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal([], schemaKeys.Except(codeKeys).OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Equal([], codeKeys.Except(schemaKeys).OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    /// <summary>`db` 아래 키도 같은 이유로 맞춰야 한다.</summary>
    [Fact]
    public void Schema_DbPropertiesMatchTheOptionsRecord()
    {
        var schema = ReadSchema();
        var schemaKeys = schema.RootElement
            .GetProperty("properties").GetProperty("db").GetProperty("properties").EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] expected =
        [
            "queryApis", "cacheApis", "connectionFactories", "shardKeyParameters", "externalApis",
            "transactionWrappers", "cacheWrappers", "dataSources", "compensation", "queryBuilders"
        ];

        Assert.Equal([], schemaKeys.Except(expected).OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Equal([], expected.Except(schemaKeys).OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    /// <summary>`init`이 만든 파일은 동작을 바꾸지 않아야 한다. 빈 배열로 기본값을 지워 버리면 안 된다.</summary>
    [Fact]
    public async Task Template_DoesNotChangeBehaviour()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-init-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, TraceLensConfig.FileName),
                TraceLensConfig.BuildTemplate("./tracelens.config.schema.json"));

            var result = await TraceLensConfig.LoadAsync(directory);

            Assert.Equal([], result.Issues.Select(issue => issue.Message).ToArray());
            Assert.Equal(TraceLensConfig.Default.Db.QueryApis, result.Config.Db.QueryApis);
            Assert.Equal(TraceLensConfig.Default.Db.CacheApis, result.Config.Db.CacheApis);
            Assert.Equal(TraceLensConfig.Default.MaxCallDepth, result.Config.MaxCallDepth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonDocument ReadSchema() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(GetRepositoryRoot(), "docs", "tracelens.config.schema.json")));

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}

/// <summary>
/// 기본 analysisScope=solution이라 테스트 프로젝트의 타입이 클래스 다이어그램에 섞인다.
/// 제외 수단이 실제로 동작하는지, 그리고 뺀 프로젝트가 진단에 남는지 확인한다 —
/// 조용히 사라지면 "왜 이 타입이 없지"를 알 수 없다.
/// </summary>
[Collection(FixtureClassicAppCollection.Name)]
public sealed class ProjectExclusionTests
{
    [Fact]
    public async Task ExcludeProjectNamePatterns_RemovesMatchingProjectsButKeepsThemInDiagnostics()
    {
        var root = Path.Combine(GetTestsRoot(), "FixturesClassic");
        var configPath = Path.Combine(root, TraceLensConfig.FileName);
        await File.WriteAllTextAsync(configPath, "{ \"excludeProjectNamePatterns\": [\"FixtureClassicApp\"] }");
        try
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddLogging();
            services.AddTraceLensAnalyzer();
            await using var provider = services.BuildServiceProvider();

            var result = await provider.GetRequiredService<ITraceLensAnalyzer>()
                .AnalyzeAsync(Path.Combine(root, "FixtureClassicApp", "FixtureClassicApp.csproj"));

            // 제외했으므로 진입점도 클래스도 나오지 않는다.
            Assert.Empty(result.Graph.EntryPoints);

            // 그러나 "무엇을 뺐는지"는 진단에 남는다.
            var excluded = result.Graph.Diagnostics!.Projects.Single(project => project.Name == "FixtureClassicApp");
            Assert.False(excluded.Loaded);
            Assert.Contains(excluded.SampleErrors, line => line.Contains("excludeProjectNamePatterns", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    private static string GetTestsRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetDirectoryName(sourcePath)!;
}

using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// 수집기 다섯 개가 같은 문서를 다시 바인딩하지 않도록 구문 트리·시맨틱 모델을 공유하는 캐시.
/// 여기서 확인하는 것은 "같은 것을 돌려준다"이지 속도가 아니다. 속도 수치는 CHANGELOG에 측정 조건과 함께 적는다.
/// </summary>
[Collection(FixtureAppCollection.Name)]
public sealed class SemanticCacheTests
{
    [Fact]
    public async Task GetSemantics_ReturnsSameInstanceForSameDocument()
    {
        await using var loaded = await LoadAsync();
        var document = AnalyzableDocuments(loaded).First();

        var first = await loaded.GetSemanticsAsync(document, CancellationToken.None);
        var second = await loaded.GetSemanticsAsync(document, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Same(first!.Model, second!.Model);
    }

    [Fact]
    public async Task Prewarm_FillsEveryAnalyzableDocument()
    {
        await using var loaded = await LoadAsync();
        var documents = AnalyzableDocuments(loaded).ToArray();

        await loaded.PrewarmSemanticsAsync(3000, CancellationToken.None);

        Assert.Equal(documents.Length, loaded.PrewarmedDocuments);
        foreach (var document in documents)
        {
            Assert.NotNull(await loaded.GetSemanticsAsync(document, CancellationToken.None));
        }
    }

    /// <summary>문서가 상한을 넘으면 준비 단계를 건너뛴다. 건너뛰어도 조회는 그대로 된다.</summary>
    [Fact]
    public async Task Prewarm_SkipsWhenDocumentCountExceedsLimit()
    {
        await using var loaded = await LoadAsync();

        await loaded.PrewarmSemanticsAsync(1, CancellationToken.None);

        Assert.Equal(0, loaded.PrewarmedDocuments);
        var document = AnalyzableDocuments(loaded).First();
        Assert.NotNull(await loaded.GetSemanticsAsync(document, CancellationToken.None));
    }

    private static IEnumerable<Document> AnalyzableDocuments(LoadedSolution loaded) => loaded.Solution.Projects
        .Where(project => loaded.AnalyzableProjectIds.Contains(project.Id))
        .SelectMany(project => project.Documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular));

    private static async Task<LoadedSolution> LoadAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceLensAnalyzer();
        await using var provider = services.BuildServiceProvider();
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "FixtureApp", "FixtureApp.csproj");
        return await provider.GetRequiredService<SolutionLoader>().LoadAsync(path);
    }
}

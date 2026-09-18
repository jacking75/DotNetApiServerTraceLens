using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// <c>FixturesClassic</c>을 쓰는 테스트를 한 줄로 세운다.
/// 프로젝트 제외 테스트가 그 폴더에 <c>tracelens.config.json</c>을 잠깐 쓰기 때문에,
/// 같은 픽스처를 동시에 분석하면 설정이 섞여 결과가 흔들린다.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FixtureClassicAppCollection
{
    public const string Name = "FixtureClassicApp";
}

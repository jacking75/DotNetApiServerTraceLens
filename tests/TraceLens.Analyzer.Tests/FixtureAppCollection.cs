using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// <c>Fixtures/FixtureApp</c>을 분석하는 테스트를 한 줄로 세운다.
///
/// 설정 테스트가 <c>Fixtures/tracelens.config.json</c>을 잠깐 썼다 지우는데,
/// 같은 픽스처를 동시에 분석하면 (1) 그 설정이 남의 분석에 섞이고
/// (2) 분석이 파일을 열고 있는 동안 삭제가 <c>IOException</c>으로 실패한다.
/// <see cref="FixtureClassicAppCollection"/>과 같은 이유다.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FixtureAppCollection
{
    public const string Name = "FixtureApp";
}

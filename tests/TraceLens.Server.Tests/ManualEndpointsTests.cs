using TraceLens.Server.Endpoints;
using Xunit;

namespace TraceLens.Server.Tests;

public sealed class ManualEndpointsTests
{
    /// <summary>
    /// 앱의 "사용 설명서" 링크가 열리려면 두 언어 파일이 실제로 있어야 한다.
    /// 파일 이름이 바뀌거나 배포물에서 빠지면 여기서 잡는다.
    /// </summary>
    [Theory]
    [InlineData("ko")]
    [InlineData("en")]
    public void Manual_ResolvesFileForEachLanguage(string lang)
    {
        Assert.True(ManualEndpoints.Files.TryGetValue(lang, out var fileName));

        var path = ManualEndpoints.ResolveManualPath(fileName!);

        Assert.NotNull(path);
        Assert.EndsWith(fileName, path);
    }

    [Fact]
    public void Manual_UnknownFileResolvesToNull()
    {
        Assert.Null(ManualEndpoints.ResolveManualPath("manual_does_not_exist.html"));
    }
}

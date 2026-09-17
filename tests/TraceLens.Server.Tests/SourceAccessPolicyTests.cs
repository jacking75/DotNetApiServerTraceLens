using TraceLens.Analyzer.Models;
using TraceLens.Context;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// 소스 접근 규칙은 Server와 MCP가 함께 쓴다. 여기서 막히지 않으면 가져온 JSON의 경로 하나로
/// 로컬 파일을 읽을 수 있게 되므로 경로 탈출 경로를 하나씩 확인한다.
/// </summary>
public sealed class SourceAccessPolicyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tracelens-policy-" + Guid.NewGuid().ToString("N"));
    private readonly string outside = Path.Combine(Path.GetTempPath(), "tracelens-outside-" + Guid.NewGuid().ToString("N"));

    public SourceAccessPolicyTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Src"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(root, "Src", "Service.cs"), "class Service { }");
        File.WriteAllText(Path.Combine(outside, "Secret.cs"), "secret");
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
    }

    [Fact]
    public void Check_AllowsFileInsideSnapshot()
    {
        var result = Policy().Check("Src/Service.cs");

        Assert.True(result.Allowed);
        Assert.Equal(SourceAccessDenial.None, result.Denial);
    }

    [Theory]
    [InlineData("../Secret.cs")]
    [InlineData("..\\Secret.cs")]
    [InlineData("Src/../../Secret.cs")]
    public void Check_DeniesParentTraversal(string file)
    {
        var result = Policy().Check(file);

        Assert.False(result.Allowed);
    }

    [Theory]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\file.cs")]
    public void Check_DeniesAbsoluteAndUncPaths(string file)
    {
        var result = Policy().Check(file);

        Assert.False(result.Allowed);
        Assert.Equal(SourceAccessDenial.OutsideRoot, result.Denial);
    }

    [Fact]
    public void Check_DeniesFileNotInTheSnapshot()
    {
        File.WriteAllText(Path.Combine(root, "Src", "Extra.cs"), "class Extra { }");

        var result = Policy().Check("Src/Extra.cs");

        Assert.False(result.Allowed);
        Assert.Equal(SourceAccessDenial.NotInSnapshot, result.Denial);
    }

    [Fact]
    public void Check_DeniesMissingFile()
    {
        var graph = CreateGraph() with
        {
            FileHashes = new Dictionary<string, string> { ["Src/Gone.cs"] = "sha256:x" }
        };

        var result = SourceAccessPolicy.ForGraph(graph).Check("Src/Gone.cs");

        Assert.False(result.Allowed);
        Assert.Equal(SourceAccessDenial.NotFound, result.Denial);
    }

    /// <summary>대소문자가 달라도 같은 파일이다(Windows). 목록 비교는 OrdinalIgnoreCase여야 한다.</summary>
    [Fact]
    public void Check_IsCaseInsensitiveForTheAllowList()
    {
        var result = Policy().Check("src/service.cs");

        Assert.True(result.Allowed);
    }

    /// <summary>가져온 분석 결과(offlineSnapshot)에는 어떤 경로도 열어 주지 않는다.</summary>
    [Fact]
    public void Check_DeniesEverythingForUntrustedGraph()
    {
        var result = SourceAccessPolicy.ForGraph(CreateGraph(), trusted: false).Check("Src/Service.cs");

        Assert.False(result.Allowed);
        Assert.Contains("offlineSnapshot", result.Message, StringComparison.Ordinal);
    }

    /// <summary>루트 안의 심볼릭 링크가 루트 밖을 가리키면 막는다. 만들지 못하는 환경이면 건너뛴다.</summary>
    [Fact]
    public void Check_DeniesSymlinkEscapingTheRoot()
    {
        var linkPath = Path.Combine(root, "Src", "Link.cs");
        try
        {
            File.CreateSymbolicLink(linkPath, Path.Combine(outside, "Secret.cs"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Windows에서 개발자 모드가 아니면 심볼릭 링크를 만들 수 없다. 그 환경에서는 검사할 것이 없다.
            return;
        }

        var graph = CreateGraph() with
        {
            FileHashes = new Dictionary<string, string>
            {
                ["Src/Service.cs"] = "sha256:x",
                ["Src/Link.cs"] = "sha256:y"
            }
        };

        var result = SourceAccessPolicy.ForGraph(graph).Check("Src/Link.cs");

        Assert.False(result.Allowed);
        Assert.Equal(SourceAccessDenial.OutsideRoot, result.Denial);
    }

    [Fact]
    public void Check_DeniesWhenRootIsUnknown()
    {
        var graph = CreateGraph() with { SolutionPath = string.Empty, RootDirectory = string.Empty };

        var result = SourceAccessPolicy.ForGraph(graph).Check("Src/Service.cs");

        Assert.False(result.Allowed);
    }

    private SourceAccessPolicy Policy() => SourceAccessPolicy.ForGraph(CreateGraph());

    private GraphDocument CreateGraph() => new(
        GraphDocument.CurrentSchemaVersion,
        Path.Combine(root, "Fixture.sln"),
        DateTimeOffset.Now,
        [],
        [],
        [],
        [],
        new Dictionary<string, string> { ["Src/Service.cs"] = "sha256:x" })
    {
        RootDirectory = root
    };
}

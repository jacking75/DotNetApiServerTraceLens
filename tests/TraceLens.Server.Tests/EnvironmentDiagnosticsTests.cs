using TraceLens.Analyzer;
using TraceLens.Context;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// doctor가 "분석할 수 있다"고 했는데 실제로는 실패하는 경우를 잡는다.
/// 통과시켜 놓고 뒤에서 실패하는 것이 가장 나쁜 진단이다.
/// </summary>
public sealed class EnvironmentDiagnosticsTests
{
    /// <summary>SDK 6.0만 있어도 net10.0 프로젝트는 열리지 않는다. 버전 판정은 폴더 이름에서 읽는다.</summary>
    [Theory]
    [InlineData("6.0.428", 6)]
    [InlineData("8.0.404", 8)]
    [InlineData("10.0.100-preview.1", 10)]
    public void ReadSdkVersion_ReadsMajorFromDirectoryName(string directoryName, int expectedMajor)
    {
        var version = SolutionLoader.ReadSdkVersion(directoryName);

        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version.Major);
    }

    [Fact]
    public void MinimumSdkMajor_IsTen()
    {
        // 이 값이 낮아지면 SDK_TOO_OLD가 조용히 무력해진다.
        Assert.Equal(10, SolutionLoader.MinimumSdkMajor);
    }

    /// <summary>
    /// 설치된 SDK보다 <b>새</b> 프레임워크를 요구하면 경고한다.
    /// SDK N은 netX(X ≤ N)를 빌드할 수 있으므로 "메이저가 정확히 같은가"로 보면 정상 환경이 경고로 뜬다.
    /// </summary>
    [Fact]
    public void Run_WarnsWhenTargetFrameworkHasNoInstalledSdk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "Future.csproj");
        // 현실적으로 설치돼 있을 수 없는 메이저 버전이다.
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net99.0</TargetFramework></PropertyGroup></Project>");

        try
        {
            var report = new EnvironmentDiagnostics().Run(new DiagnosticsOptions { TargetPath = project });

            Assert.Contains(report.Items, item => item.Code == "TFM_NOT_INSTALLED" && item.Detail.Contains("net99.0", StringComparison.Ordinal));
            // 경고이지 오류가 아니다. 분석을 막지는 않는다.
            Assert.Equal(DiagnosticSeverity.Warning, report.Items.Single(item => item.Code == "TFM_NOT_INSTALLED").Severity);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 서버 안에서 부른 환경 점검은 자기 자신이 듣고 있는 포트를 문제로 보고하면 안 된다(T48).
    /// "이미 실행 중인 TraceLens를 종료하라"는 자기 자신을 가리키는 처방이라 신뢰를 깎는다.
    /// </summary>
    [Fact]
    public void Run_DoesNotReportOwnPortAsInUse()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var inServer = new EnvironmentDiagnostics().Run(new DiagnosticsOptions { Port = port, SelfListening = true });
            var selfItem = Assert.Single(inServer.Items, item => item.Code.StartsWith("PORT_", StringComparison.Ordinal));
            Assert.Equal("PORT_SELF", selfItem.Code);
            Assert.Equal(DiagnosticSeverity.Ok, selfItem.Severity);

            // CLI `doctor`는 서버를 띄우기 전이라 기존 경고를 그대로 낸다.
            var fromCli = new EnvironmentDiagnostics().Run(new DiagnosticsOptions { Port = port });
            Assert.Contains(fromCli.Items, item => item.Code == "PORT_IN_USE");
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>설치된 SDK로 빌드되는 프로젝트에는 경고가 붙으면 안 된다. 이 오탐이 실제로 났었다.</summary>
    [Fact]
    public void Run_DoesNotWarnForAnInstalledTargetFramework()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "Current.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        try
        {
            var report = new EnvironmentDiagnostics().Run(new DiagnosticsOptions { TargetPath = project });

            Assert.DoesNotContain(report.Items, item => item.Code == "TFM_NOT_INSTALLED");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

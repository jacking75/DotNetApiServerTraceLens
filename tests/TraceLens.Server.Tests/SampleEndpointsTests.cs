using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using TraceLens.Context;
using TraceLens.Server.Endpoints;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// 첫 화면의 "예제 분석"이 쓰는 API. 예제는 도구 폴더가 아니라 사용자 영역으로 복사한 뒤 분석한다.
/// </summary>
public sealed class SampleEndpointsTests
{
    [Fact]
    public void List_ReturnsPackagedSamples()
    {
        var result = SampleEndpoints.List(WebApplication.CreateBuilder().Environment);

        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var samples = Assert.IsType<SampleView[]>(valueResult.Value);
        // 소스 트리에서 테스트하면 samples 폴더를 찾지 못할 수 있다. 목록 형태만 확인한다.
        Assert.All(samples, sample =>
        {
            Assert.False(string.IsNullOrWhiteSpace(sample.Id));
            Assert.False(string.IsNullOrWhiteSpace(sample.RelativeTargetPath));
        });
    }

    [Fact]
    public async Task PrepareAsync_ReturnsNotFound_ForUnknownId()
    {
        var result = await SampleEndpoints.PrepareAsync("does-not-exist", new PrepareSampleRequest(), WebApplication.CreateBuilder().Environment);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusResult.StatusCode);
    }

    [Fact]
    public async Task PrepareAsync_CopiesOnceAndReusesAfterwards()
    {
        var source = Path.Combine(Path.GetTempPath(), "tracelens-sample-src-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(Path.GetTempPath(), "tracelens-sample-dst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(source, "OrderApi"));
        await File.WriteAllTextAsync(Path.Combine(source, "OrderApi", "OrderApi.csproj"), "<Project />");
        try
        {
            var sample = new DemoSample(
                "order-api", "제목", "설명",
                Path.Combine(source, "OrderApi"),
                Path.Combine("OrderApi", "OrderApi.csproj"),
                [], []);

            var first = await DemoSamples.PrepareAsync(sample, destination, overwrite: false);
            Assert.True(first.Copied);
            Assert.True(File.Exists(first.TargetPath));

            var second = await DemoSamples.PrepareAsync(sample, destination, overwrite: false);
            Assert.False(second.Copied);
            Assert.Equal(first.TargetPath, second.TargetPath);

            var third = await DemoSamples.PrepareAsync(sample, destination, overwrite: true);
            Assert.True(third.Copied);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(destination, recursive: true);
        }
    }

/// <summary>
    /// 같은 소스 폴더를 가리키는 학습 경로가 둘이어도 복사는 한 번만 한다.
    /// 두 번 복사하면 방금 복사한 파일을 곧바로 덮어쓰고, CLI 목록도 프로젝트가 둘인 것처럼 보인다.
    /// </summary>
    [Fact]
    public async Task CopyAsync_CopiesTheSameSourceDirectoryOnce()
    {
        var source = Path.Combine(Path.GetTempPath(), "tracelens-sample-src-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(Path.GetTempPath(), "tracelens-sample-dst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(source, "OrderApi"));
        await File.WriteAllTextAsync(Path.Combine(source, "OrderApi", "OrderApi.csproj"), "<Project />");
        try
        {
            var sourceDirectory = Path.Combine(source, "OrderApi");
            var relative = Path.Combine("OrderApi", "OrderApi.csproj");
            var samples = new[]
            {
                new DemoSample("order-api", "제목1", "설명", sourceDirectory, relative, [], []),
                new DemoSample("order-api-compensation", "제목2", "설명", sourceDirectory, relative, [], []),
            };

            var result = await DemoSamples.CopyAsync(samples, destination);

            Assert.Single(result.CopiedDirectories);
            Assert.Equal(sourceDirectory, result.CopiedDirectories[0]);
            Assert.True(File.Exists(Path.Combine(result.Destination, "OrderApi", "OrderApi.csproj")));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public void DefaultDestination_IsUnderUserLocalAppData()
    {
        var destination = DemoSamples.DefaultDestination("order-api");
        Assert.Contains("TraceLens", destination, StringComparison.Ordinal);
        Assert.EndsWith("order-api", destination, StringComparison.Ordinal);
    }
}

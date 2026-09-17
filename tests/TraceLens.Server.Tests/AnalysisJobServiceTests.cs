using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;
using TraceLens.Server.Endpoints;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// 분석 작업의 수명(진행·취소·중복 거절·목록 상한)을 확인한다.
/// 이전에는 작업이 딕셔너리에 무제한으로 쌓였고 같은 대상을 두 번 시작하면 둘 다 돌았다.
/// </summary>
public sealed class AnalysisJobServiceTests
{
    [Fact]
    public async Task Start_RejectsSecondJobForTheSameTarget()
    {
        var analyzer = new BlockingAnalyzer();
        var service = CreateService(analyzer);

        var first = service.Start("C:\\Fixture\\Fixture.sln");
        Assert.False(first.AlreadyRunning);
        await analyzer.Started;

        var second = service.Start("C:\\Fixture\\Fixture.sln");
        Assert.True(second.AlreadyRunning);
        Assert.Equal(first.Job.Id, second.Job.Id);

        analyzer.Release();
        await WaitUntilFinishedAsync(service, first.Job.Id);
        Assert.Equal(1, analyzer.Calls);
    }

    [Fact]
    public async Task Start_RunsAgainAfterTheFirstJobFinished()
    {
        var analyzer = new BlockingAnalyzer();
        var service = CreateService(analyzer);

        var first = service.Start("C:\\Fixture\\Fixture.sln");
        await analyzer.Started;
        analyzer.Release();
        await WaitUntilFinishedAsync(service, first.Job.Id);

        var second = service.Start("C:\\Fixture\\Fixture.sln");
        Assert.False(second.AlreadyRunning);
        Assert.NotEqual(first.Job.Id, second.Job.Id);
    }

    [Fact]
    public async Task Start_KeepsOnlyTheMostRecentFinishedJobs()
    {
        var analyzer = new BlockingAnalyzer { ReleaseImmediately = true };
        var service = CreateService(analyzer);

        var ids = new List<string>();
        for (var index = 0; index < 25; index++)
        {
            var started = service.Start($"C:\\Fixture\\Fixture{index}.sln");
            ids.Add(started.Job.Id);
            await WaitUntilFinishedAsync(service, started.Job.Id);
        }

        // 상한(20) 밖의 옛 작업은 지워진다. 목록이 무제한으로 자라지 않는다.
        Assert.True(service.List().Count <= 21, $"작업이 {service.List().Count}개 남았다.");
        Assert.Null(service.Get(ids[0]));
        Assert.NotNull(service.Get(ids[^1]));
    }

    [Fact]
    public async Task StartEndpoint_ReturnsConflict_WhenTheSameTargetIsRunning()
    {
        var analyzer = new BlockingAnalyzer();
        var service = CreateService(analyzer);
        var request = new AnalyzeRequest("C:\\Fixture\\Fixture.sln");

        AnalysisJobEndpoints.Start(request, service);
        await analyzer.Started;
        var second = AnalysisJobEndpoints.Start(request, service);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(second);
        Assert.Equal(StatusCodes.Status409Conflict, statusResult.StatusCode);
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(second);
        Assert.Equal(AnalysisJobEndpoints.JobAlreadyRunning, Assert.IsType<ApiError>(valueResult.Value).Code);

        analyzer.Release();
    }

    private static AnalysisJobService CreateService(ITraceLensAnalyzer analyzer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(analyzer);
        var provider = services.BuildServiceProvider();
        return new AnalysisJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new GraphStore(),
            NullLogger<AnalysisJobService>.Instance);
    }

    private static async Task WaitUntilFinishedAsync(IAnalysisJobService service, string jobId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (service.Get(jobId) is not { State: AnalysisJobState.Running })
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"작업 {jobId}이(가) 끝나지 않았다.");
    }

    /// <summary>테스트가 끝나는 시점을 정하는 분석기 스텁.</summary>
    private sealed class BlockingAnalyzer : ITraceLensAnalyzer
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ReleaseImmediately { get; init; }

        public Task Started => started.Task;

        public int Calls { get; private set; }

        public void Release() => release.TrySetResult();

        public async Task<AnalysisResult> AnalyzeAsync(
            string solutionPath,
            IProgress<AnalysisProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            started.TrySetResult();
            if (!ReleaseImmediately)
            {
                await release.Task.WaitAsync(cancellationToken);
            }

            var graph = new GraphDocument(
                GraphDocument.CurrentSchemaVersion,
                solutionPath,
                DateTimeOffset.Now,
                [],
                [],
                [],
                [],
                new Dictionary<string, string>());
            return new AnalysisResult(graph, 1, "graph.json", 0, 0, 0, 0);
        }
    }
}

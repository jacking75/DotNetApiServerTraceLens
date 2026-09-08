using System.Collections.Concurrent;
using System.Threading.Channels;
using TraceLens.Analyzer;
using TraceLens.Server.Endpoints;

namespace TraceLens.Server;

public enum AnalysisJobState
{
    Running,
    Succeeded,
    Failed,
    Canceled
}

/// <param name="Kind">progress · done · error · canceled 중 하나.</param>
public sealed record AnalysisJobEvent(string Kind, string Stage, string Message, int? Count = null)
{
    public object? Result { get; init; }
}

public sealed record AnalysisJob(
    string Id,
    string TargetPath,
    DateTimeOffset StartedAt)
{
    public AnalysisJobState State { get; set; } = AnalysisJobState.Running;

    public string? Error { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}

public interface IAnalysisJobService
{
    AnalysisJob Start(string targetPath);

    AnalysisJob? Get(string jobId);

    bool Cancel(string jobId);

    IAsyncEnumerable<AnalysisJobEvent> WatchAsync(string jobId, CancellationToken cancellationToken);
}

/// <summary>
/// 분석을 작업으로 돌려 진행 상태를 SSE로 보내고 취소를 받는다.
/// 취소가 끝날 때까지 기존 그래프를 유지하고, 성공했을 때만 <see cref="IGraphStore"/>를 교체한다.
/// </summary>
public sealed class AnalysisJobService(
    IServiceScopeFactory scopeFactory,
    IGraphStore graphStore,
    ILogger<AnalysisJobService> logger) : IAnalysisJobService
{
    private sealed class JobContext
    {
        public required AnalysisJob Job { get; init; }

        public required CancellationTokenSource Cancellation { get; init; }

        public required List<AnalysisJobEvent> History { get; init; }

        public required List<Channel<AnalysisJobEvent>> Listeners { get; init; }

        public required object Gate { get; init; }
    }

    private readonly ConcurrentDictionary<string, JobContext> jobs = new(StringComparer.Ordinal);

    public AnalysisJob Start(string targetPath)
    {
        var job = new AnalysisJob(Guid.NewGuid().ToString("N")[..12], targetPath, DateTimeOffset.Now);
        var context = new JobContext
        {
            Job = job,
            Cancellation = new CancellationTokenSource(),
            History = [],
            Listeners = [],
            Gate = new object()
        };
        jobs[job.Id] = context;
        _ = Task.Run(() => RunAsync(context));
        return job;
    }

    public AnalysisJob? Get(string jobId) => jobs.TryGetValue(jobId, out var context) ? context.Job : null;

    public bool Cancel(string jobId)
    {
        if (!jobs.TryGetValue(jobId, out var context) || context.Job.State != AnalysisJobState.Running)
        {
            return false;
        }

        context.Cancellation.Cancel();
        return true;
    }

    public async IAsyncEnumerable<AnalysisJobEvent> WatchAsync(
        string jobId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!jobs.TryGetValue(jobId, out var context))
        {
            yield break;
        }

        var channel = Channel.CreateUnbounded<AnalysisJobEvent>();
        List<AnalysisJobEvent> replay;
        lock (context.Gate)
        {
            replay = [.. context.History];
            if (context.Job.State == AnalysisJobState.Running)
            {
                context.Listeners.Add(channel);
            }
            else
            {
                channel.Writer.TryComplete();
            }
        }

        // 늦게 붙은 클라이언트도 지금까지의 단계를 볼 수 있게 지난 이벤트를 먼저 보낸다.
        foreach (var item in replay)
        {
            yield return item;
        }

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    private async Task RunAsync(JobContext context)
    {
        using var scope = scopeFactory.CreateScope();
        var analyzer = scope.ServiceProvider.GetRequiredService<ITraceLensAnalyzer>();
        var progress = new Progress<AnalysisProgress>(item =>
            Publish(context, new AnalysisJobEvent("progress", item.Stage, item.Message, item.Count)));

        try
        {
            var result = await analyzer.AnalyzeAsync(context.Job.TargetPath, progress, context.Cancellation.Token);
            graphStore.Set(result.Graph);
            context.Job.State = AnalysisJobState.Succeeded;
            context.Job.FinishedAt = DateTimeOffset.Now;
            Publish(context, new AnalysisJobEvent("done", "save", "분석을 마쳤다.")
            {
                Result = AnalyzeEndpoints.BuildSummary(result)
            });
        }
        catch (OperationCanceledException)
        {
            context.Job.State = AnalysisJobState.Canceled;
            context.Job.FinishedAt = DateTimeOffset.Now;
            logger.LogInformation("분석 작업 {JobId}을(를) 취소했다. 기존 그래프는 그대로 둔다.", context.Job.Id);
            Publish(context, new AnalysisJobEvent("canceled", "cancel", "분석을 취소했다. 이전 분석 결과를 그대로 쓴다."));
        }
        catch (Exception exception)
        {
            context.Job.State = AnalysisJobState.Failed;
            context.Job.Error = exception.Message;
            context.Job.FinishedAt = DateTimeOffset.Now;
            logger.LogWarning(exception, "분석 작업 {JobId}이(가) 실패했다.", context.Job.Id);
            Publish(context, new AnalysisJobEvent("error", "error", exception.Message));
        }
        finally
        {
            Complete(context);
        }
    }

    private static void Publish(JobContext context, AnalysisJobEvent item)
    {
        lock (context.Gate)
        {
            context.History.Add(item);
            foreach (var listener in context.Listeners)
            {
                listener.Writer.TryWrite(item);
            }
        }
    }

    private static void Complete(JobContext context)
    {
        lock (context.Gate)
        {
            foreach (var listener in context.Listeners)
            {
                listener.Writer.TryComplete();
            }

            context.Listeners.Clear();
        }
    }
}

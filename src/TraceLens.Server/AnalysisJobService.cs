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

    /// <summary>
    /// 실패 이벤트의 분류 코드. UI가 이 코드로 한 줄 요약과 다음 행동을 고른다.
    /// 예외 원문은 <see cref="Message"/>에 그대로 남긴다(접어 두고 보여 준다).
    /// </summary>
    public string? Code { get; init; }
}

/// <summary>
/// 분석 실패 예외를 UI가 다음 행동을 고를 수 있는 코드로 분류한다.
/// 분류하지 못하면 <c>ANALYZE_FAILED</c>로 두고 원문을 그대로 전달한다 — 없는 원인을 지어내지 않는다.
/// </summary>
public static class AnalysisFailureClassifier
{
    public const string SdkNotFound = "SDK_NOT_FOUND";
    public const string RestoreFailed = "RESTORE_FAILED";
    public const string FileLocked = "FILE_LOCKED";
    public const string TargetNotFound = "TARGET_NOT_FOUND";
    public const string AnalyzeFailed = "ANALYZE_FAILED";

    public static string Classify(Exception exception)
    {
        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return TargetNotFound;
        }

        var message = exception.Message;
        if (Contains(message, "MSBuildLocator") || Contains(message, ".NET SDK") ||
            Contains(message, "MSBuild instance") || Contains(message, "dotnet --list-sdks"))
        {
            return SdkNotFound;
        }

        if (Contains(message, "NETSDK1004") || Contains(message, "project.assets.json") ||
            Contains(message, "NuGet") || Contains(message, "dotnet restore"))
        {
            return RestoreFailed;
        }

        if (exception is IOException || Contains(message, "being used by another process") ||
            Contains(message, "다른 프로세스에서 사용 중") || Contains(message, "sharing violation"))
        {
            return FileLocked;
        }

        if (Contains(message, "찾을 수 없다") || Contains(message, "could not be found") ||
            Contains(message, "does not exist"))
        {
            return TargetNotFound;
        }

        return AnalyzeFailed;
    }

    private static bool Contains(string text, string needle) =>
        text.Contains(needle, StringComparison.OrdinalIgnoreCase);
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

/// <param name="AlreadyRunning">같은 대상의 작업이 이미 돌고 있어 새로 시작하지 않았다면 true다(<paramref name="Job"/>은 그 작업).</param>
public sealed record StartJobResult(AnalysisJob Job, bool AlreadyRunning);

public interface IAnalysisJobService
{
    /// <summary>같은 대상의 작업이 이미 돌고 있으면 새로 시작하지 않고 그 작업을 돌려준다.</summary>
    StartJobResult Start(string targetPath);

    AnalysisJob? Get(string jobId);

    bool Cancel(string jobId);

    /// <summary>지금 들고 있는 작업 목록(최근 것부터). 상한을 넘은 옛 작업은 이미 지워졌다.</summary>
    IReadOnlyList<AnalysisJob> List();

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

    /// <summary>끝난 작업을 이 개수만 남긴다. 그 외에는 아래 보관 시간이 지나면 지운다.</summary>
    private const int MaxFinishedJobs = 20;

    /// <summary>끝난 작업을 이 시간까지만 들고 있는다. 진행 상태를 늦게 보러 오는 탭을 위해 바로 지우지는 않는다.</summary>
    private static readonly TimeSpan FinishedJobRetention = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, JobContext> jobs = new(StringComparer.Ordinal);

    public StartJobResult Start(string targetPath)
    {
        // 같은 대상을 두 번 분석하면 둘 다 돌고 나중 것이 이긴다. 그럴 바에는 거절하고 진행 중인 작업을 알려 준다.
        var running = jobs.Values.FirstOrDefault(item =>
            item.Job.State == AnalysisJobState.Running &&
            string.Equals(item.Job.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase));
        if (running is not null)
        {
            return new StartJobResult(running.Job, true);
        }

        PruneFinishedJobs();
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
        return new StartJobResult(job, false);
    }

    public AnalysisJob? Get(string jobId) => jobs.TryGetValue(jobId, out var context) ? context.Job : null;

    public IReadOnlyList<AnalysisJob> List() => jobs.Values
        .Select(item => item.Job)
        .OrderByDescending(item => item.StartedAt)
        .ToArray();

    /// <summary>끝난 작업이 계속 쌓이지 않게 정리한다. 보관 시간이 지났거나 최근 20개 밖이면 지운다.</summary>
    private void PruneFinishedJobs()
    {
        var finished = jobs.Values
            .Where(item => item.Job.State != AnalysisJobState.Running)
            .OrderByDescending(item => item.Job.FinishedAt ?? item.Job.StartedAt)
            .ToArray();
        var now = DateTimeOffset.Now;
        for (var index = 0; index < finished.Length; index++)
        {
            var job = finished[index].Job;
            var expired = job.FinishedAt is { } at && now - at > FinishedJobRetention;
            if (index < MaxFinishedJobs && !expired)
            {
                continue;
            }

            if (jobs.TryRemove(job.Id, out var removed))
            {
                removed.Cancellation.Dispose();
            }
        }
    }

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
            Publish(context, new AnalysisJobEvent("error", "error", exception.Message)
            {
                Code = AnalysisFailureClassifier.Classify(exception)
            });
        }
        finally
        {
            Complete(context);
            context.Cancellation.Dispose();
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

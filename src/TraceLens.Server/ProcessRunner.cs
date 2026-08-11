using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace TraceLens.Server;

public interface IProcessRunner
{
    IAsyncEnumerable<ProcessOutputLine> RunStreamingAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default);

    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}

public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

public sealed record ProcessOutputLine(string Text, bool IsStandardError);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class ProcessExecutionException(int exitCode)
    : Exception($"프로세스가 종료 코드 {exitCode}(으)로 끝났다.")
{
    public int ExitCode { get; } = exitCode;
}

public sealed class ProcessRunner : IProcessRunner
{
    public async IAsyncEnumerable<ProcessOutputLine> RunStreamingAsync(
        ProcessRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var process = CreateProcess(request);
        var output = Channel.CreateUnbounded<ProcessOutputLine>();
        var standardOutputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var standardErrorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardOutputClosed.TrySetResult();
            }
            else
            {
                output.Writer.TryWrite(new ProcessOutputLine(eventArgs.Data, false));
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardErrorClosed.TrySetResult();
            }
            else
            {
                output.Writer.TryWrite(new ProcessOutputLine(eventArgs.Data, true));
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"프로세스를 시작할 수 없다: {request.FileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var completion = CompleteChannelAsync();

        try
        {
            await foreach (var line in output.Reader.ReadAllAsync(cancellationToken))
            {
                yield return line;
            }

            await completion;
            if (process.ExitCode != 0)
            {
                throw new ProcessExecutionException(process.ExitCode);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }

        async Task CompleteChannelAsync()
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(standardOutputClosed.Task, standardErrorClosed.Task);
                output.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                output.Writer.TryComplete(exception);
            }
        }
    }

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        using var process = CreateProcess(request);
        if (!process.Start())
        {
            throw new InvalidOperationException($"프로세스를 시작할 수 없다: {request.FileName}");
        }

        try
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static Process CreateProcess(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = startInfo };
    }
}

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TraceLens.Context;

/// <param name="Directory">로그를 쓸 폴더. 분석 루트를 모르는 시작 시점에는 사용자 영역을 쓴다.</param>
/// <param name="RetentionDays">이 일수가 지난 파일은 시작할 때 지운다.</param>
/// <param name="MaxFileBytes">한 파일이 이 크기를 넘으면 뒤에 번호를 붙여 새 파일로 넘어간다.</param>
public sealed record FileLoggerOptions(string Directory, int RetentionDays = 7, long MaxFileBytes = 8L * 1024 * 1024)
{
    /// <summary>파일 이름 앞부분. 서버와 MCP 로그를 같은 폴더에서 구분한다.</summary>
    public string FilePrefix { get; init; } = "tracelens";

    /// <summary>분석 루트를 아직 모를 때 쓰는 기본 위치.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TraceLens",
        "logs");
}

/// <summary>
/// 외부 패키지 없이 날짜별 파일로 쓰는 로거다. 서버와 MCP가 같은 것을 쓴다 —
/// MCP는 stderr로만 보내서 실패가 아무 데도 남지 않았다(T60).
/// 500 오류에 붙일 <c>errorId</c>가 로그에도 남아야 사용자가 무엇을 첨부해야 할지 알 수 있다.
/// 비밀값·환경 변수는 남기지 않는다(로그에 들어가는 것은 이 프로세스가 만든 문장뿐이다).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggerOptions options;
    private readonly ConcurrentQueue<string> pending = new();
    private readonly object writeGate = new();
    private readonly ConcurrentQueue<string> recentErrors = new();

    public FileLoggerProvider(FileLoggerOptions options)
    {
        this.options = options;
        Directory.CreateDirectory(options.Directory);
        PruneOldFiles();
    }

    /// <summary>오늘 로그 파일의 전체 경로. doctor가 "어디를 보면 되는지" 알려줄 때 쓴다.</summary>
    public string CurrentFilePath => Path.Combine(
        options.Directory,
        $"{options.FilePrefix}-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>최근 오류 요약(최대 3건). 전체 로그를 붙이지 않고도 무엇이 났는지 보이게 한다.</summary>
    public IReadOnlyList<string> RecentErrors => recentErrors.ToArray();

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() => Flush();

    private void Append(LogLevel level, string category, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(" [").Append(level).Append("] ")
            .Append(category).Append(" | ")
            .Append(message);
        if (exception is not null)
        {
            line.Append(Environment.NewLine).Append(exception);
        }

        var text = line.ToString();
        pending.Enqueue(text);
        if (level >= LogLevel.Error)
        {
            recentErrors.Enqueue($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {category}: {message}");
            while (recentErrors.Count > 3)
            {
                recentErrors.TryDequeue(out _);
            }
        }

        Flush();
    }

    private void Flush()
    {
        lock (writeGate)
        {
            if (pending.IsEmpty)
            {
                return;
            }

            var builder = new StringBuilder();
            while (pending.TryDequeue(out var line))
            {
                builder.AppendLine(line);
            }

            try
            {
                var path = ResolveWritablePath();
                File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 로그를 못 써도 서버는 계속 돌아야 한다. 이 실패 자체를 다시 로그로 남기지 않는다(재귀).
            }
        }
    }

    /// <summary>크기 상한을 넘으면 뒤에 번호를 붙인 파일로 넘어간다.</summary>
    private string ResolveWritablePath()
    {
        var basePath = CurrentFilePath;
        for (var index = 0; index < 100; index++)
        {
            var path = index == 0 ? basePath : basePath[..^4] + $".{index}.log";
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < options.MaxFileBytes)
            {
                return path;
            }
        }

        return basePath;
    }

    private void PruneOldFiles()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-options.RetentionDays);
            foreach (var file in Directory.EnumerateFiles(options.Directory, $"{options.FilePrefix}-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 옛 로그를 못 지워도 새 로그는 쓴다.
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Append(logLevel, category, formatter(state, exception), exception);
        }
    }
}

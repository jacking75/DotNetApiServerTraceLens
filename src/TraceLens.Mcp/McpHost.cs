using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using TraceLens.Context;

namespace TraceLens.Mcp;

/// <summary>
/// MCP 호스트 구성을 CLI(<c>tracelens mcp</c>)와 단독 실행 양쪽에서 재사용할 수 있게 뽑아 둔 진입 함수다.
/// 전송은 로컬 stdio만 쓴다.
/// </summary>
public static class McpHost
{
    public static async Task<int> RunAsync(string[] args, string? graphPath = null)
    {
        var resolvedGraphPath = graphPath ?? GraphPathArguments.Resolve(args);
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        // stdout은 MCP 프로토콜 전용이라 로그는 모두 stderr로 보낸다.
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        // stderr만 쓰면 MCP 호스트가 그 출력을 버릴 때 실패가 아무 데도 남지 않는다.
        // 서버와 같은 폴더에 파일로도 남긴다(T60).
        LogFile = new FileLoggerProvider(new FileLoggerOptions(FileLoggerOptions.DefaultDirectory)
        {
            FilePrefix = "tracelens-mcp"
        });
        builder.Logging.AddProvider(LogFile);
        builder.Services.AddSingleton(new GraphFileStore(resolvedGraphPath));
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<TraceLensTools>();

        try
        {
            await builder.Build().RunAsync();
        }
        finally
        {
            LogFile?.Dispose();
        }

        return 0;
    }

    /// <summary>이번 실행의 파일 로거. 실패를 남길 위치를 doctor가 알려 줄 수 있어야 한다.</summary>
    public static FileLoggerProvider? LogFile { get; private set; }

    /// <summary>오늘 MCP 로그 파일의 경로. 아직 시작하지 않았으면 null이다.</summary>
    public static string? LogFilePath => LogFile?.CurrentFilePath;
}

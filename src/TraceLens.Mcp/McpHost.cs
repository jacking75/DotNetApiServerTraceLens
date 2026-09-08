using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

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
        builder.Services.AddSingleton(new GraphFileStore(resolvedGraphPath));
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<TraceLensTools>();

        await builder.Build().RunAsync();
        return 0;
    }
}

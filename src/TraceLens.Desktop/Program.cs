using System.Net;
using System.Net.Sockets;
using TraceLens.Cli;
using TraceLens.Server;

namespace TraceLens.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // MCP 설정에는 이 EXE의 "mcp" 명령을 등록한다. UI 없이 stdio로 실행해야 한다.
        if (args.Length > 0 && string.Equals(args[0], "mcp", StringComparison.OrdinalIgnoreCase))
            return CommandRouter.RunAsync(args).GetAwaiter().GetResult();

        var target = ParseTarget(args);
        if (args.Length > 0 && target is null)
        {
            MessageBox.Show("사용법: TraceLens.Desktop.exe [--target <솔루션 또는 프로젝트 경로>]",
                "TraceLens", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 2;
        }

        Microsoft.AspNetCore.Builder.WebApplication? server = null;
        try
        {
            var contentRoot = DesktopAssets.Extract();
            var port = AvailablePort();
            server = ServerHost.StartAsync([], new ServerOptions
            {
                Port = port,
                ContentRootPath = contentRoot,
                InitialTargetPath = target
            }).GetAwaiter().GetResult();

            ApplicationConfiguration.Initialize();
            using var window = new DesktopWindow(port);
            Application.Run(window);
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show($"TraceLens를 시작하지 못했다.\n\n{exception.Message}",
                "TraceLens", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            if (server is not null)
            {
                server.StopAsync().GetAwaiter().GetResult();
                server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static string? ParseTarget(string[] args) =>
        args.Length == 2 && args[0] == "--target" && !string.IsNullOrWhiteSpace(args[1])
            ? args[1]
            : null;

    private static int AvailablePort()
    {
        try
        {
            using var preferred = new TcpListener(IPAddress.Loopback, ServerHost.DefaultPort);
            preferred.Start();
            return ServerHost.DefaultPort;
        }
        catch (SocketException)
        {
            using var fallback = new TcpListener(IPAddress.Loopback, 0);
            fallback.Start();
            return ((IPEndPoint)fallback.LocalEndpoint).Port;
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using TraceLens.Mcp;

var graphPath = GraphPathArguments.Resolve(args);
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new GraphFileStore(graphPath));
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<TraceLensTools>();

await builder.Build().RunAsync();

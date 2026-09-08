using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceLens.Analyzer;
using TraceLens.Context;
using TraceLens.Server.Endpoints;

namespace TraceLens.Server;

public sealed record ServerOptions
{
    /// <summary>루프백에만 바인딩한다. 원격 접근을 여는 옵션은 두지 않는다.</summary>
    public int Port { get; init; } = ServerHost.DefaultPort;

    /// <summary>시작하자마자 이 대상을 불러온다(재분석하지 않고 저장된 결과만 읽는다).</summary>
    public string? InitialTargetPath { get; init; }
}

/// <summary>
/// 서버 호스트 구성을 CLI와 <c>dotnet run</c> 양쪽에서 재사용할 수 있게 뽑아 둔 진입 함수다.
/// Analyzer는 이 호스트를 참조하지 않는다.
/// </summary>
public static class ServerHost
{
    public const int DefaultPort = 25011;

    /// <summary>Vite 개발 서버 포트. 개발 중에만 CORS를 연다.</summary>
    private const int ViteDevPort = 25012;

    public static WebApplication Build(string[] args, ServerOptions? options = null)
    {
        var serverOptions = options ?? new ServerOptions();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            // 배포 패키지에서는 어느 작업 디렉터리에서 실행해도 정적 파일을 찾아야 한다.
            ContentRootPath = ResolveContentRoot(),
            WebRootPath = "wwwroot"
        });

        builder.WebHost.UseUrls($"http://127.0.0.1:{serverOptions.Port}");
        builder.Services.AddCors(options => options.AddPolicy("ViteLocal", policy =>
            policy.WithOrigins($"http://localhost:{ViteDevPort}").AllowAnyHeader().AllowAnyMethod()));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        // members가 들어가면서 /api/graph 응답이 커졌다. 로컬이라도 수 MB JSON은 파싱 전 전송에서 체감된다.
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
        });
        builder.Services.AddSingleton<IGraphStore, GraphStore>();
        builder.Services.AddSingleton<IGraphFreshnessService, GraphFreshnessService>();
        builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
        builder.Services.AddSingleton<IAgentRunner, ClaudeCodeAgentRunner>();
        builder.Services.AddSingleton<IMcpConfigWriter, McpConfigWriter>();
        builder.Services.AddSingleton<IAnalysisJobService, AnalysisJobService>();
        builder.Services.AddTraceLensAnalyzer();
        builder.Services.AddTraceLensContext();

        var app = builder.Build();
        app.UseResponseCompression();
        app.UseCors("ViteLocal");
        app.UseDefaultFiles();
        app.UseStaticFiles();

        var api = app.MapGroup("/api");
        api.MapAnalyzeEndpoints();
        api.MapAnalysisJobEndpoints();
        api.MapBrowseEndpoints();
        api.MapGraphEndpoints();
        api.MapSourceEndpoints();
        api.MapFreshnessEndpoints();
        api.MapAskEndpoints();
        api.MapContextEndpoints();
        api.MapImpactEndpoints();
        api.MapSnapshotEndpoints();
        api.MapDiagnosticsEndpoints();

        if (app.Environment.IsProduction())
        {
            app.MapFallbackToFile("index.html");
        }

        return app;
    }

    public static async Task<int> RunAsync(string[] args, ServerOptions? options = null)
    {
        var app = Build(args, options);
        if (!string.IsNullOrWhiteSpace(options?.InitialTargetPath))
        {
            await LoadSavedGraphAsync(app, options.InitialTargetPath);
        }

        await app.RunAsync();
        return 0;
    }

    /// <summary>저장해 둔 분석 결과가 있으면 재분석 없이 되살린다. 없으면 조용히 넘어간다.</summary>
    private static async Task LoadSavedGraphAsync(WebApplication app, string targetPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
            if (directory is null)
            {
                return;
            }

            var loaded = await GraphJsonStore.LoadAsync(Path.Combine(directory, ".tracelens", "graph.json"));
            if (loaded.Ok && loaded.Graph is not null)
            {
                app.Services.GetRequiredService<IGraphStore>().Set(loaded.Graph);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 첫 실행처럼 저장된 결과가 없으면 분석 전 상태로 그대로 둔다.
        }
    }

    /// <summary>
    /// 정적 파일(wwwroot)의 기준 디렉터리를 실행 파일 위치로 고정한다.
    /// 대상 프로젝트 폴더에서 <c>tracelens serve</c>를 실행해도 UI가 열려야 한다.
    /// </summary>
    private static string ResolveContentRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return Directory.Exists(Path.Combine(baseDirectory, "wwwroot"))
            ? baseDirectory
            : Directory.GetCurrentDirectory();
    }
}

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

    /// <summary>데스크톱 단일 파일이 내장 웹 자산을 풀어 둔 경로. 비우면 기존 배포 경로를 쓴다.</summary>
    public string? ContentRootPath { get; init; }
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

    /// <summary>이번 실행이 쓰는 로그 파일 경로. doctor와 진단 API가 알려 준다.</summary>
    public static string? LogFilePath { get; private set; }

    /// <summary>
    /// 이번 실행이 실제로 바인딩한 포트.
    /// 진단이 항상 <see cref="DefaultPort"/>를 검사하면 <c>--port 25099</c>로 띄운 서버가
    /// 쓰지도 않는 25011에 대해 PORT_IN_USE를 말한다 — 틀린 정보다.
    /// </summary>
    public static int BoundPort { get; private set; } = DefaultPort;

    /// <summary>최근 오류 요약. 전체 로그를 붙이지 않고도 무엇이 났는지 보이게 한다.</summary>
    public static IReadOnlyList<string> RecentErrors => fileLogger?.RecentErrors ?? [];

    private static FileLoggerProvider? fileLogger;

    public static WebApplication Build(string[] args, ServerOptions? options = null)
    {
        var serverOptions = options ?? new ServerOptions();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            // 배포 패키지에서는 어느 작업 디렉터리에서 실행해도 정적 파일을 찾아야 한다.
            ContentRootPath = serverOptions.ContentRootPath ?? ResolveContentRoot(),
            WebRootPath = "wwwroot"
        });

        BoundPort = serverOptions.Port;
        builder.WebHost.UseUrls($"http://127.0.0.1:{serverOptions.Port}");
        builder.Services.AddCors(options => options.AddPolicy("ViteLocal", policy =>
            policy.WithOrigins($"http://localhost:{ViteDevPort}").AllowAnyHeader().AllowAnyMethod()));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        // 본문이 JSON이 아니면 ASP.NET이 기본적으로 본문 없는 400을 낸다.
        // 예외로 올려야 RequestGuards가 다른 오류와 같은 `{code,message}`로 바꿀 수 있다(T72).
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        // members가 들어가면서 /api/graph 응답이 커졌다. 로컬이라도 수 MB JSON은 파싱 전 전송에서 체감된다.
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
        });
        // 분석 루트는 아직 모르므로 사용자 영역에 쓴다. 500 응답의 errorId를 여기서 되짚을 수 있어야 한다.
        fileLogger = new FileLoggerProvider(new FileLoggerOptions(FileLoggerOptions.DefaultDirectory));
        LogFilePath = fileLogger.CurrentFilePath;
        builder.Logging.AddProvider(fileLogger);

        builder.Services.AddSingleton<IGraphStore, GraphStore>();
        builder.Services.AddSingleton<IGraphFreshnessService, GraphFreshnessService>();
        builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
        // 러너는 이름 있는 집합이다. doctor가 찾은 CLI만 화면의 드롭다운에 보인다(T70).
        builder.Services.AddSingleton<IAgentRunner, ClaudeCodeAgentRunner>();
        builder.Services.AddSingleton<IAgentRunner, CodexAgentRunner>();
        builder.Services.AddSingleton<IAgentRunnerRegistry, AgentRunnerRegistry>();
        builder.Services.AddSingleton<IMcpConfigWriter, McpConfigWriter>();
        builder.Services.AddSingleton<IAnalysisJobService, AnalysisJobService>();
        builder.Services.AddTraceLensAnalyzer();
        builder.Services.AddTraceLensContext();

        var app = builder.Build();
        app.UseTraceLensGuards(serverOptions.Port, ViteDevPort);
        app.UseResponseCompression();
        app.UseCors("ViteLocal");
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapManualEndpoints();

        var api = app.MapGroup("/api");
        api.MapAnalyzeEndpoints();
        api.MapAnalysisJobEndpoints();
        api.MapBrowseEndpoints();
        api.MapSampleEndpoints();
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
            // `/api/*`는 SPA로 떨어뜨리지 않는다. 없는 엔드포인트에 HTML 200을 돌려주면
            // 스크립트·에이전트가 "성공했는데 응답이 이상하다"로 읽는다.
            app.MapFallback("/api/{**rest}", () => ApiResults.Error(
                StatusCodes.Status404NotFound,
                "ENDPOINT_NOT_FOUND",
                "그런 API가 없다. docs/data-contract.md의 HTTP API 표를 확인한다."));
            app.MapFallbackToFile("index.html");
        }

        return app;
    }

    public static async Task<int> RunAsync(string[] args, ServerOptions? options = null)
    {
        await using var app = await StartAsync(args, options);
        await app.WaitForShutdownAsync();
        return 0;
    }

    /// <summary>데스크톱 창이 서버의 수명과 주소를 직접 관리할 때 쓴다.</summary>
    public static async Task<WebApplication> StartAsync(
        string[] args,
        ServerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var app = Build(args, options);
        try
        {
            if (!string.IsNullOrWhiteSpace(options?.InitialTargetPath))
            {
                await LoadSavedGraphAsync(app, options.InitialTargetPath);
            }

            await app.StartAsync(cancellationToken);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    /// <summary>저장해 둔 분석 결과가 있으면 재분석 없이 되살린다. 없으면 조용히 넘어간다.</summary>
    private static async Task LoadSavedGraphAsync(WebApplication app, string targetPath)
    {
        try
        {
            var location = await GraphLocator.FindForTargetAsync(targetPath);
            if (location.GraphPath is not { } graphPath)
            {
                return;
            }

            var loaded = await GraphJsonStore.LoadAsync(graphPath);
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

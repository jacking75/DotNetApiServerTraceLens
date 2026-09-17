using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// 루프백이어도 브라우저의 다른 페이지가 분석 시작이나 CLI 실행을 부를 수 있다.
/// 상태를 바꾸는 <c>/api/*</c> 요청은 같은 출처에서 온 것만 받는다. MCP는 stdio라 이 검사와 무관하다.
/// </summary>
public sealed class RequestGuardsTests
{
    [Theory]
    [InlineData("POST", "/api/analysis-jobs", "cross-site", null, StatusCodes.Status403Forbidden)]
    [InlineData("POST", "/api/ask", "cross-site", "http://evil.example", StatusCodes.Status403Forbidden)]
    [InlineData("POST", "/api/analysis-jobs", "same-origin", null, StatusCodes.Status200OK)]
    [InlineData("POST", "/api/analysis-jobs", "none", null, StatusCodes.Status200OK)]
    [InlineData("POST", "/api/analysis-jobs", null, null, StatusCodes.Status200OK)]
    [InlineData("GET", "/api/graph", "cross-site", "http://evil.example", StatusCodes.Status200OK)]
    [InlineData("POST", "/not-api", "cross-site", "http://evil.example", StatusCodes.Status200OK)]
    public async Task Guard_BlocksOnlyCrossSiteStateChangingApiCalls(
        string method, string path, string? site, string? origin, int expected)
    {
        var context = await RunAsync(method, path, site, origin);

        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Fact]
    public async Task Guard_AllowsTheViteDevServerOrigin()
    {
        var context = await RunAsync("POST", "/api/analysis-jobs", "cross-site", "http://localhost:25012");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    /// <summary>잡지 않은 예외는 500 + errorId로 바뀌고 같은 id가 로그에도 남는다.</summary>
    [Fact]
    public async Task Guard_TurnsUnhandledExceptionIntoErrorIdAndLogsIt()
    {
        var logger = new CapturingLoggerProvider();
        var context = await RunAsync("GET", "/api/boom", null, null, logger, throwOnRequest: true);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains(RequestGuards.Unexpected, body, StringComparison.Ordinal);

        var errorId = System.Text.RegularExpressions.Regex.Match(body, "\"errorId\":\"([0-9a-f]{8})\"").Groups[1].Value;
        Assert.NotEqual(string.Empty, errorId);
        Assert.Contains(logger.Lines, line => line.Contains(errorId, StringComparison.Ordinal));
    }

    private static async Task<HttpContext> RunAsync(
        string method,
        string path,
        string? site,
        string? origin,
        CapturingLoggerProvider? loggerProvider = null,
        bool throwOnRequest = false)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (loggerProvider is not null) builder.AddProvider(loggerProvider);
        });
        var provider = services.BuildServiceProvider();

        var builder = new ApplicationBuilder(provider);
        builder.UseTraceLensGuards(25011, 25012);
        builder.Run(_ => throwOnRequest
            ? throw new InvalidOperationException("일부러 낸 오류")
            : Task.CompletedTask);

        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            Request = { Method = method, Path = path },
            Response = { Body = new MemoryStream() }
        };
        if (site is not null) context.Request.Headers["Sec-Fetch-Site"] = site;
        if (origin is not null) context.Request.Headers.Origin = origin;

        await builder.Build()(context);
        return context;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturing(this);

        public void Dispose()
        {
        }

        private sealed class Capturing(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                provider.Lines.Add(formatter(state, exception));
        }
    }
}

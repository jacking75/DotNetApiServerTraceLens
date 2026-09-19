using TraceLens.Analyzer;
using TraceLens.Analyzer.Models;
using TraceLens.Context;

namespace TraceLens.Server.Endpoints;

public static class DiagnosticsEndpoints
{
    public static RouteGroupBuilder MapDiagnosticsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/diagnostics", GetAsync);
        return group;
    }

    /// <summary>
    /// 환경과 현재 분석 능력을 돌려준다. 비밀값을 반환하지 않고 설치·설정 변경을 실행하지 않는다.
    /// </summary>
    internal static Task<IResult> GetAsync(
        string? target,
        IGraphStore store,
        EnvironmentDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var targetPath = string.IsNullOrWhiteSpace(target) ? store.Current?.SolutionPath : target;
        var report = diagnostics.Run(new DiagnosticsOptions
        {
            TargetPath = targetPath,
            Port = ServerHost.BoundPort,
            // 이 점검은 서버 안에서 돈다. 그 포트의 리스너는 이 프로세스 자신이다(T48).
            SelfListening = ServerHost.BoundPort > 0,
            McpHostCandidates = McpConfigWriter.GetHostCandidates()
        });
        // 500 오류에 붙일 것을 사용자가 알 수 있도록 로그 위치와 최근 오류를 함께 준다(비밀값은 담지 않는다).
        return Task.FromResult<IResult>(Results.Ok(report with
        {
            Log = new DiagnosticsLogInfo(ServerHost.LogFilePath, ServerHost.RecentErrors)
        }));
    }
}

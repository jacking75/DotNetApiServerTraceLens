using System.Runtime.CompilerServices;

namespace TraceLens.Server;

/// <param name="Kind">delta(응답 조각) 또는 log(실행 로그).</param>
public sealed record AgentOutput(string Kind, string Text);

public sealed record AgentRequest(
    string Prompt,
    string WorkingDirectory,
    string McpConfigPath,
    IReadOnlyList<string> AllowedTools)
{
    /// <summary>쓸 모델. 비우면 CLI 기본값을 그대로 쓴다.</summary>
    public string? Model { get; init; }
}

/// <summary>
/// 인앱 실행이 의존하는 외부 CLI 부분만 분리한 인터페이스다.
/// 러너는 이름이 있고 여럿 등록된다 — <c>doctor</c>가 찾은 CLI만 화면의 드롭다운에 보인다(T70).
/// </summary>
public interface IAgentRunner
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>실행 파일 이름. <c>doctor</c>의 <c>CLI_*</c> 점검과 같은 값이다.</summary>
    string Command { get; }

    IAsyncEnumerable<AgentOutput> RunAsync(AgentRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 등록된 러너 집합. <c>agent</c> 파라미터로 고르고, 없으면 첫 번째를 쓴다.
/// </summary>
public interface IAgentRunnerRegistry
{
    IReadOnlyList<IAgentRunner> Runners { get; }

    /// <summary>id로 러너를 고른다. 비우면 첫 번째다. 없는 id면 null이다.</summary>
    IAgentRunner? Resolve(string? id);
}

public sealed class AgentRunnerRegistry(IEnumerable<IAgentRunner> runners) : IAgentRunnerRegistry
{
    public IReadOnlyList<IAgentRunner> Runners { get; } = runners.ToArray();

    public IAgentRunner? Resolve(string? id) => string.IsNullOrWhiteSpace(id)
        ? Runners.FirstOrDefault()
        : Runners.FirstOrDefault(runner => string.Equals(runner.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// CLI 인자 조립을 러너 본문에서 떼어 낸 순수 함수 모음.
/// 테스트가 실제 <c>--help</c> 출력과 대조할 수 있어야 해서 따로 둔다(T70).
/// </summary>
public static class AgentArguments
{
    /// <summary>
    /// Claude Code의 인자.
    /// <c>--strict-mcp-config</c>로 이 저장소의 MCP만 쓰게 하고, 허용 도구를 명시한다.
    /// </summary>
    public static string[] ForClaudeCode(AgentRequest request)
    {
        var args = new List<string>
        {
            "-p", request.Prompt,
            "--mcp-config", request.McpConfigPath,
            "--strict-mcp-config",
            "--allowedTools", string.Join(',', request.AllowedTools),
            "--permission-mode", "dontAsk",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--no-session-persistence"
        };
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            args.AddRange(["--model", request.Model]);
        }

        return [.. args];
    }

    /// <summary>
    /// Codex CLI의 인자. <c>codex exec</c>는 한 번 실행하고 끝나는 비대화 모드다.
    /// MCP 설정은 <c>-c</c>로 넘긴 TOML 조각이 아니라 사용자의 <c>~/.codex/config.toml</c>을 쓴다 —
    /// 우리가 그 파일을 대신 고치지 않는다(설정 변경은 사용자가 한다).
    /// </summary>
    public static string[] ForCodex(AgentRequest request)
    {
        var args = new List<string> { "exec", "--skip-git-repo-check" };
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            args.AddRange(["--model", request.Model]);
        }

        args.Add(request.Prompt);
        return [.. args];
    }

    /// <summary>
    /// 러너가 쓰는 플래그 이름만 뽑는다(값은 뺀다).
    /// 테스트가 CLI <c>--help</c> 출력에 이 이름들이 있는지 대조한다.
    /// </summary>
    public static IReadOnlyList<string> FlagsOf(IEnumerable<string> args) => args
        .Where(arg => arg.StartsWith("--", StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

public sealed class ClaudeCodeAgentRunner(IProcessRunner processRunner) : IAgentRunner
{
    public string Id => "claude-code";

    public string DisplayName => "Claude Code";

    public string Command => "claude";

    public async IAsyncEnumerable<AgentOutput> RunAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var processRequest = new ProcessRequest(
            Command,
            AgentArguments.ForClaudeCode(request),
            request.WorkingDirectory);

        await foreach (var output in processRunner.RunStreamingAsync(processRequest, cancellationToken))
        {
            if (output.IsStandardError)
            {
                yield return new AgentOutput("log", output.Text);
                continue;
            }

            var parsed = ClaudeStreamParser.Parse(output.Text);
            if (parsed is not null && !string.IsNullOrEmpty(parsed.Text))
            {
                yield return new AgentOutput(parsed.Kind, parsed.Text);
            }
        }
    }
}

/// <summary>
/// Codex CLI 러너. <c>doctor</c>가 <c>codex</c>를 찾아 놓고도 인앱 실행은 <c>claude</c> 고정이라
/// Codex 사용자는 "AI에게 질문"의 실행 버튼을 못 썼다(T70).
///
/// Codex의 <c>exec</c>는 평문을 표준 출력으로 흘린다. Claude Code의 stream-json 파서를 쓰지 않는다.
/// </summary>
public sealed class CodexAgentRunner(IProcessRunner processRunner) : IAgentRunner
{
    public string Id => "codex";

    public string DisplayName => "Codex CLI";

    public string Command => "codex";

    public async IAsyncEnumerable<AgentOutput> RunAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var processRequest = new ProcessRequest(
            Command,
            AgentArguments.ForCodex(request),
            request.WorkingDirectory);

        await foreach (var output in processRunner.RunStreamingAsync(processRequest, cancellationToken))
        {
            yield return output.IsStandardError
                ? new AgentOutput("log", output.Text)
                : new AgentOutput("delta", output.Text);
        }
    }
}

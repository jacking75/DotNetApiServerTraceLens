using System.Runtime.CompilerServices;

namespace TraceLens.Server;

/// <param name="Kind">delta(응답 조각) 또는 log(실행 로그).</param>
public sealed record AgentOutput(string Kind, string Text);

public sealed record AgentRequest(
    string Prompt,
    string WorkingDirectory,
    string McpConfigPath,
    IReadOnlyList<string> AllowedTools);

/// <summary>
/// 인앱 실행이 의존하는 외부 CLI 부분만 분리한 인터페이스다.
/// 첫 구현은 기존 Claude Code 호출을 그대로 옮긴 것뿐이며, 여러 CLI의 인자·인증·스트림 형식을 동시에 떠안지 않는다.
/// </summary>
public interface IAgentRunner
{
    string Id { get; }

    string DisplayName { get; }

    IAsyncEnumerable<AgentOutput> RunAsync(AgentRequest request, CancellationToken cancellationToken);
}

public sealed class ClaudeCodeAgentRunner(IProcessRunner processRunner) : IAgentRunner
{
    public string Id => "claude-code";

    public string DisplayName => "Claude Code";

    public async IAsyncEnumerable<AgentOutput> RunAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var processRequest = new ProcessRequest(
            "claude",
            [
                "-p", request.Prompt,
                "--mcp-config", request.McpConfigPath,
                "--strict-mcp-config",
                "--allowedTools", string.Join(',', request.AllowedTools),
                "--permission-mode", "dontAsk",
                "--output-format", "stream-json",
                "--verbose",
                "--include-partial-messages",
                "--no-session-persistence"
            ],
            request.WorkingDirectory);

        await foreach (var output in processRunner.RunStreamingAsync(processRequest, cancellationToken))
        {
            if (output.IsStandardError)
            {
                yield return new AgentOutput("log", output.Text);
                continue;
            }

            var text = ClaudeStreamParser.TryGetText(output.Text);
            if (!string.IsNullOrEmpty(text))
            {
                yield return new AgentOutput("delta", text);
            }
        }
    }
}

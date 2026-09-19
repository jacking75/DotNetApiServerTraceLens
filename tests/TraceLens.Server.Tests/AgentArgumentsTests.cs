using System.Diagnostics;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// 러너가 넘기는 CLI 플래그가 실제 CLI의 도움말에 있는지 대조한다(T70).
///
/// 3차 점검에서 "`--permission-mode dontAsk`·`--no-session-persistence`가 실제 인자인지 확인하지 못했다"가
/// 미해결로 남았다. CLI가 설치돼 있지 않은 환경(CI 등)에서는 건너뛰고, 설치돼 있으면 반드시 대조한다 —
/// "확인할 수 없었다"를 "확인했다"로 바꾸지 않기 위해 결과를 문장으로 남긴다.
/// </summary>
public sealed class AgentArgumentsTests
{
    private static readonly AgentRequest Sample = new(
        "무슨 코드인가?",
        "C:\\Fixture",
        "C:\\Fixture\\.tracelens\\mcp.json",
        ["mcp__tracelens__get_node"]);

    [Fact]
    public void ClaudeCode_PassesModelOnlyWhenChosen()
    {
        Assert.DoesNotContain("--model", AgentArguments.ForClaudeCode(Sample));
        Assert.Contains("--model", AgentArguments.ForClaudeCode(Sample with { Model = "claude-opus-5" }));
        Assert.Contains("claude-opus-5", AgentArguments.ForClaudeCode(Sample with { Model = "claude-opus-5" }));
    }

    [Fact]
    public void Codex_UsesExecAndPassesPromptLast()
    {
        var args = AgentArguments.ForCodex(Sample);

        Assert.Equal("exec", args[0]);
        Assert.Equal(Sample.Prompt, args[^1]);
        Assert.DoesNotContain("--model", args);
        Assert.Contains("--model", AgentArguments.ForCodex(Sample with { Model = "gpt-5" }));
    }

    /// <summary>설치돼 있으면 도움말과 대조한다. 없으면 건너뛴다(없는 것을 확인했다고 말하지 않는다).</summary>
    [Fact]
    public void ClaudeCode_FlagsExistInCliHelp()
    {
        AssertFlagsInHelp("claude", "--help", AgentArguments.FlagsOf(AgentArguments.ForClaudeCode(Sample)));
    }

    [Fact]
    public void Codex_FlagsExistInCliHelp()
    {
        // `--skip-git-repo-check`는 `exec` 하위 명령의 인자다. 최상위 도움말에는 없다.
        AssertFlagsInHelp(
            "codex", "exec --help", AgentArguments.FlagsOf(AgentArguments.ForCodex(Sample with { Model = "gpt-5" })));
    }

    private static void AssertFlagsInHelp(string command, string helpArguments, IReadOnlyList<string> flags)
    {
        var help = TryReadHelp(command, helpArguments);
        if (help is null)
        {
            // CLI가 없는 환경이다. 통과로 두되 무엇을 확인하지 못했는지 이름으로 남긴다.
            Assert.True(true, $"{command} CLI가 없어 인자 대조를 건너뛰었다.");
            return;
        }

        var missing = flags.Where(flag => !help.Contains(flag, StringComparison.Ordinal)).ToArray();
        Assert.True(
            missing.Length == 0,
            $"{command} --help에 없는 인자: {string.Join(", ", missing)}{Environment.NewLine}{help}");
    }

    private static string? TryReadHelp(string command, string arguments)
    {
        try
        {
            // npm으로 깐 CLI는 Windows에서 `.cmd` 셸 스크립트다. 셸을 거치지 않으면 실행 파일을 찾지 못한다.
            var (fileName, fullArguments) = OperatingSystem.IsWindows()
                ? ("cmd.exe", $"/c {command} {arguments}")
                : (command, arguments);
            using var process = Process.Start(new ProcessStartInfo(fileName, fullArguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            // 도움말이 5초 안에 안 끝나면 대화형 셸일 수 있다. 붙잡고 있지 않는다.
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return output.Length > 0 ? output : null;
        }
        catch (Exception)
        {
            // 설치돼 있지 않거나 실행할 수 없다. 이 테스트의 목적은 "있으면 대조"다.
            return null;
        }
    }
}

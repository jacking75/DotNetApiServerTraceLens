using System.Reflection;
using TraceLens.Cli.Commands;
using Xunit;

namespace TraceLens.Cli.Tests;

/// <summary>
/// 종료 코드가 `--help`·README·계약 문서에 그대로 적혀 있는지 확인한다(T63).
///
/// 자동화는 종료 코드로 분기한다. 코드를 더하거나 뜻을 바꾸고 문서를 안 고치면
/// 스크립트가 조용히 틀린 분기를 탄다.
/// </summary>
public sealed class ExitCodeDocumentationTests
{
    [Fact]
    public async Task Help_ListsEveryExitCode()
    {
        var usage = await CaptureHelpAsync();
        var section = Section(usage, "종료 코드");

        foreach (var (code, _) in ExitCodes())
        {
            Assert.Contains(code.ToString(), section, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("README_kr.md", "| 종료 코드 |")]
    [InlineData("README.md", "| Exit code |")]
    [InlineData("docs/data-contract.md", "| 종료 코드 |")]
    public void Documents_ListEveryExitCode(string relativePath, string tableHeader)
    {
        var text = ReadRepositoryFile(relativePath);
        Assert.Contains(tableHeader, text, StringComparison.Ordinal);

        foreach (var (code, name) in ExitCodes())
        {
            Assert.True(
                text.Contains($"| `{code}` |", StringComparison.Ordinal),
                $"{relativePath}에 종료 코드 {code}({name}) 행이 없다.");
        }
    }

    /// <summary>계약 문서는 상수 이름까지 적는다 — 코드에서 문서로 찾아갈 수 있게.</summary>
    [Fact]
    public void Contract_NamesTheConstants()
    {
        var text = ReadRepositoryFile("docs/data-contract.md");

        foreach (var (_, name) in ExitCodes())
        {
            Assert.Contains($"GraphCommands.{name}", text, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<(int Code, string Name)> ExitCodes() => typeof(GraphCommands)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(int))
        .Select(field => ((int)field.GetRawConstantValue()!, field.Name));

    private static async Task<string> CaptureHelpAsync()
    {
        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            await CommandRouter.RunAsync(["--help"]);
        }
        finally
        {
            Console.SetOut(original);
        }

        return captured.ToString();
    }

    private static string Section(string text, string heading)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"도움말에 \"{heading}\" 절이 없다.{Environment.NewLine}{text}");
        return text[start..];
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"문서를 찾지 못했다: {path}");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}

using System.Reflection;
using ModelContextProtocol.Server;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// `docs/data-contract.md`의 "MCP 도구" 표와 실제 `[McpServerTool]`을 대조한다.
///
/// 계약 문서가 "HTTP·MCP 계약을 한곳에"라고 선언해 놓고 MCP는 산문 설명뿐이었다(T62).
/// 문서와 코드가 어긋나면 에이전트가 없는 도구를 부르거나 있는 도구를 못 찾는다.
/// </summary>
public sealed class McpToolContractTests
{
    [Fact]
    public void Document_ListsEveryTool()
    {
        var declared = DeclaredTools();
        var documented = DocumentedTools();

        Assert.NotEmpty(declared);
        Assert.Equal([], declared.Except(documented, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal([], documented.Except(declared, StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// 표에 적은 파라미터가 실제 시그니처에 있어야 한다.
    /// 이름이 바뀌었는데 문서만 남으면 에이전트가 없는 인자를 넘긴다.
    /// </summary>
    [Fact]
    public void Document_NamesRealParameters()
    {
        var document = ReadContract();
        var missing = new List<string>();

        foreach (var method in ToolMethods())
        {
            var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
            var row = document
                .Split('\n')
                .FirstOrDefault(line => line.StartsWith($"| `{attribute.Name}`", StringComparison.Ordinal));
            if (row is null)
            {
                continue;
            }

            var parameters = method.GetParameters().Select(item => item.Name!).ToHashSet(StringComparer.Ordinal);
            foreach (var name in ParameterNames(row))
            {
                if (!parameters.Contains(name))
                {
                    missing.Add($"{attribute.Name}: {name}");
                }
            }
        }

        Assert.Equal([], missing);
    }

    /// <summary>`get_context`가 받는 선택 종류마다 id를 찾을 도구가 있어야 한다.</summary>
    [Fact]
    public void Document_MapsEverySelectionKindToADiscoveryTool()
    {
        var document = ReadContract();
        var kinds = Enum.GetNames<TraceLens.Context.SelectionKind>();

        foreach (var kind in kinds)
        {
            var name = char.ToLowerInvariant(kind[0]) + kind[1..];
            Assert.Contains($"| `{name}` |", document, StringComparison.Ordinal);
        }
    }

    private static IReadOnlyCollection<MethodInfo> ToolMethods() => typeof(TraceLens.Mcp.TraceLensTools)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
        .ToArray();

    private static IReadOnlyCollection<string> DeclaredTools() => ToolMethods()
        .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name!)
        .ToArray();

    /// <summary>표의 첫 칸에 적은 도구 이름. `get_callers` / `get_callees`처럼 한 줄에 둘을 적기도 한다.</summary>
    private static IReadOnlyCollection<string> DocumentedTools()
    {
        var section = ContractSection();
        var names = new List<string>();
        foreach (var line in section.Split('\n').Where(line => line.StartsWith("| `", StringComparison.Ordinal)))
        {
            var cell = line.Split('|')[1];
            foreach (var part in cell.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim().Trim('`');
                // selectionKind 표의 행은 도구 이름이 아니다. 도구 이름에만 있는 밑줄로 가른다.
                if (name.Contains('_', StringComparison.Ordinal))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private static IEnumerable<string> ParameterNames(string row) => row
        .Split('|')[2]
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Trim().Trim('`'))
        .Where(part => part.Length > 0 && !part.Contains(' ', StringComparison.Ordinal) && part != "—");

    private static string ContractSection()
    {
        var document = ReadContract();
        var start = document.IndexOf("## 7. MCP 도구", StringComparison.Ordinal);
        Assert.True(start >= 0, "계약 문서에 \"## 7. MCP 도구\" 절이 없다.");
        var end = document.IndexOf("**`selectionKind`와 발견 도구.**", start, StringComparison.Ordinal);
        return end < 0 ? document[start..] : document[start..end];
    }

    private static string ReadContract()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "data-contract.md");
        Assert.True(File.Exists(path), $"계약 문서를 찾지 못했다: {path}");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}

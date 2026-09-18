using TraceLens.Server.Endpoints;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// 인앱 실행의 허용 목록과 실제 MCP 도구가 어긋나면 UI에서만 조용히 안 불린다.
/// 목록을 특성에서 만들도록 바꿨으니, 그 연결이 끊기지 않았는지 여기서 지킨다.
/// </summary>
public sealed class McpToolAllowListTests
{
    [Fact]
    public void AllowedMcpTools_CoversEveryDeclaredTool()
    {
        var declared = typeof(TraceLens.Mcp.TraceLensTools)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .SelectMany(method => method.GetCustomAttributes(inherit: false)
                .OfType<ModelContextProtocol.Server.McpServerToolAttribute>())
            .Select(attribute => $"mcp__tracelens__{attribute.Name}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);
        Assert.Equal(declared.OrderBy(name => name, StringComparer.Ordinal), AskEndpoints.AllowedMcpTools);
    }

    /// <summary>문서와 README가 "도구 열두 개"라고 적는다. 수가 바뀌면 문서도 함께 고쳐야 한다.</summary>
    [Fact]
    public void AllowedMcpTools_HasTwelveTools()
    {
        Assert.Equal(12, AskEndpoints.AllowedMcpTools.Count);
    }
}

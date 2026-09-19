using System.Text.RegularExpressions;
using Xunit;

namespace TraceLens.Server.Tests;

/// <summary>
/// `docs/data-contract.md`의 HTTP API 표와 실제 `Endpoints/*.cs`의 `Map*` 호출을 대조한다(T72).
///
/// 문서에만 있는 엔드포인트는 쓰는 쪽을 헤매게 하고, 코드에만 있는 엔드포인트는
/// 아무도 모르는 채로 남는다. 실제로 둘 다 있었다(`/api/analysis-status`·`/api/graph/classes`).
/// </summary>
public sealed class HttpContractTests
{
    [Fact]
    public void Document_ListsEveryMappedRoute()
    {
        var code = MappedRoutes();
        var documented = DocumentedRoutes();

        Assert.NotEmpty(code);
        Assert.Equal([], code.Except(documented, StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Document_HasNoRouteThatCodeDoesNotMap()
    {
        var code = MappedRoutes();
        var documented = DocumentedRoutes();

        Assert.Equal([], documented.Except(code, StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    /// <summary>`Endpoints/*.cs`가 실제로 여는 경로. `RouteGroupBuilder`에 붙는 것은 `/api` 아래다.</summary>
    private static IReadOnlyCollection<string> MappedRoutes()
    {
        var routes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(ServerSourceRoot(), "Endpoints"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            // 그룹(`/api`)에 붙는 파일인지 앱 루트에 붙는 파일인지로 접두사가 갈린다.
            var prefix = text.Contains("this RouteGroupBuilder", StringComparison.Ordinal) ? "/api" : string.Empty;
            foreach (Match match in Regex.Matches(text, @"Map(Get|Post|Put|Delete)\(""([^""]+)"""))
            {
                routes.Add($"{match.Groups[1].Value.ToUpperInvariant()} {prefix}{match.Groups[2].Value}");
            }
        }

        return routes;
    }

    /// <summary>
    /// 표 첫 칸의 백틱 토큰. `GET/POST /api/snapshots`처럼 메서드가 둘이거나,
    /// `` `GET /api/graph`, `/graph/entrypoints` ``처럼 경로만 이어 적기도 한다.
    /// </summary>
    private static IReadOnlyCollection<string> DocumentedRoutes()
    {
        var routes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var line in Section().Split('\n').Where(item => item.StartsWith("| `", StringComparison.Ordinal)))
        {
            string[] methods = [];
            foreach (Match token in Regex.Matches(line.Split('|')[1], "`([^`]+)`"))
            {
                var parts = token.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var path = parts[^1];
                if (!path.StartsWith('/'))
                {
                    continue;
                }

                // 경로만 적힌 토큰은 바로 앞 토큰의 메서드를 이어 쓴다.
                if (parts.Length > 1)
                {
                    methods = parts[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
                }

                // `/graph/entrypoints`처럼 `/api`를 생략하고 이어 적은 경로를 되살린다.
                var full = path.StartsWith("/api/", StringComparison.Ordinal) || path.StartsWith("/manual", StringComparison.Ordinal)
                    ? path
                    : "/api" + path;
                foreach (var method in methods)
                {
                    routes.Add($"{method} {full}");
                }
            }
        }

        return routes;
    }

    private static string Section()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "data-contract.md");
        var document = File.ReadAllText(path);
        var start = document.IndexOf("## 5. HTTP API", StringComparison.Ordinal);
        Assert.True(start >= 0, "계약 문서에 \"## 5. HTTP API\" 절이 없다.");
        var end = document.IndexOf("### 오류 코드", start, StringComparison.Ordinal);
        return end < 0 ? document[start..] : document[start..end];
    }

    private static string ServerSourceRoot() =>
        Path.Combine(RepositoryRoot(), "src", "TraceLens.Server");

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}

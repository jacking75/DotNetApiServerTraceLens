using Microsoft.CodeAnalysis;

namespace TraceLens.Analyzer;

/// <param name="ProjectKey">어셈블리 이름. 여러 실행 가능한 프로젝트를 구분한다.</param>
/// <param name="Kind">program(최상위 문·Main) · startup(Startup.Configure) · program+startup(둘 다).</param>
/// <param name="Documents">읽을 문서. 소스 순서대로 Program.cs → Startup.cs.</param>
public sealed record HostDocument(IReadOnlyList<Document> Documents, string ProjectKey, string Kind)
{
    /// <summary>대표 문서. 화면에 "이 파일을 읽었다"로 보여 주는 값이다.</summary>
    public Document Document => Documents[0];

    /// <summary>
    /// <c>Startup</c> 후보가 여럿이라 그리지 않은 파일. 조용히 버리면 두 번째 파이프라인이 사유 없이 사라진다.
    /// 호출자가 <c>limitations</c>에 옮긴다(T57).
    /// </summary>
    public IReadOnlyList<string> SkippedStartups { get; init; } = [];
}

/// <summary>
/// DI 등록과 미들웨어 파이프라인이 적힌 파일을 찾는다.
/// 솔루션에 실행 가능한 프로젝트가 여럿이면 각각 하나씩 찾는다 — 첫 <c>Program.cs</c> 하나만 보면
/// 두 번째 서비스의 파이프라인이 통째로 빠진다.
///
/// .NET Core 3.1~5 구조(<c>Program.cs</c>의 <c>CreateHostBuilder</c> + <c>Startup.cs</c>)에서는
/// 등록과 파이프라인이 <c>Startup.cs</c>에 있다. 그래서 <c>Program.cs</c>를 찾아도 거기서 멈추지 않고
/// 같은 프로젝트의 <c>Startup</c>을 함께 읽는다 — 예전에는 여기서 멈춰 두 보기가 통째로 비었다.
/// </summary>
public static class HostDocuments
{
    public static async Task<IReadOnlyList<HostDocument>> FindAsync(
        LoadedSolution loaded, CancellationToken cancellationToken = default)
    {
        var result = new List<HostDocument>();
        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = await FindInProjectAsync(project, cancellationToken);
            if (found is not null)
            {
                result.Add(found);
            }
        }

        return result;
    }

    private static async Task<HostDocument?> FindInProjectAsync(Project project, CancellationToken cancellationToken)
    {
        Document? program = null;
        string? startupTypeName = null;
        var startups = new List<Document>();

        foreach (var document in project.Documents.Where(item => item.SourceCodeKind == SourceCodeKind.Regular))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(document.FilePath);
            var text = (await document.GetTextAsync(cancellationToken)).ToString();

            if (program is null &&
                string.Equals(fileName, "Program.cs", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("CreateBuilder", StringComparison.Ordinal) ||
                 text.Contains("CreateHostBuilder", StringComparison.Ordinal) ||
                 text.Contains("CreateDefaultBuilder", StringComparison.Ordinal)))
            {
                program = document;
                startupTypeName = ReadUseStartupTypeName(text);
                continue;
            }

            // 등록·파이프라인이 실제로 적힌 파일. 클래식 구조에서는 여기에만 있다.
            if (text.Contains("Configure(IApplicationBuilder", StringComparison.Ordinal) ||
                text.Contains("ConfigureServices(IServiceCollection", StringComparison.Ordinal))
            {
                startups.Add(document);
            }
        }

        // UseStartup<T>()로 이름을 알면 그 타입을 선언한 파일을 우선한다. 파일 이름이 Startup.cs가 아닐 수 있다.
        if (startupTypeName is not null)
        {
            var named = await FindDeclaringDocumentAsync(startups, startupTypeName, cancellationToken);
            if (named is not null)
            {
                startups = [named];
            }
        }

        // Startup 후보가 여럿이면 첫 것만 그린다. 조용히 버리면 두 번째 파이프라인이 사유 없이 사라진다.
        var skipped = startups.Count > 1
            ? startups.Skip(1)
                .Select(document => document.FilePath ?? document.Name)
                .ToArray()
            : [];

        if (program is null)
        {
            return startups.Count == 0
                ? null
                : new HostDocument([startups[0]], project.AssemblyName, "startup") { SkippedStartups = skipped };
        }

        return startups.Count == 0
            ? new HostDocument([program], project.AssemblyName, "program")
            : new HostDocument([program, startups[0]], project.AssemblyName, "program+startup")
            {
                SkippedStartups = skipped
            };
    }

    /// <summary><c>UseStartup&lt;Startup&gt;()</c>에서 타입 이름을 읽는다. 없으면 null.</summary>
    private static string? ReadUseStartupTypeName(string text)
    {
        const string marker = "UseStartup<";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var open = start + marker.Length;
        var close = text.IndexOf('>', open);
        if (close < 0)
        {
            return null;
        }

        var name = text[open..close].Trim();
        // 네임스페이스를 붙여 썼다면 마지막 조각만 쓴다.
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name.Length == 0 ? null : name;
    }

    private static async Task<Document?> FindDeclaringDocumentAsync(
        IReadOnlyList<Document> candidates, string typeName, CancellationToken cancellationToken)
    {
        foreach (var document in candidates)
        {
            var text = (await document.GetTextAsync(cancellationToken)).ToString();
            if (text.Contains($"class {typeName}", StringComparison.Ordinal))
            {
                return document;
            }
        }

        return null;
    }
}

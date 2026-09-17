using Microsoft.CodeAnalysis;

namespace TraceLens.Analyzer;

/// <param name="ProjectKey">어셈블리 이름. 여러 실행 가능한 프로젝트를 구분한다.</param>
/// <param name="Kind">program(최상위 문·Main) 또는 startup(Startup.Configure).</param>
public sealed record HostDocument(Document Document, string ProjectKey, string Kind);

/// <summary>
/// DI 등록과 미들웨어 파이프라인이 적힌 파일을 찾는다.
/// 솔루션에 실행 가능한 프로젝트가 여럿이면 각각 하나씩 찾는다 — 첫 <c>Program.cs</c> 하나만 보면
/// 두 번째 서비스의 파이프라인이 통째로 빠진다.
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
        Document? startup = null;
        foreach (var document in project.Documents.Where(item => item.SourceCodeKind == SourceCodeKind.Regular))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(document.FilePath);
            var text = (await document.GetTextAsync(cancellationToken)).ToString();

            if (string.Equals(fileName, "Program.cs", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("CreateBuilder", StringComparison.Ordinal) ||
                 text.Contains("CreateHostBuilder", StringComparison.Ordinal) ||
                 text.Contains("CreateDefaultBuilder", StringComparison.Ordinal)))
            {
                return new HostDocument(document, project.AssemblyName, "program");
            }

            // 옛 형태(Startup 클래스)도 같은 내용을 담는다. Program.cs를 못 찾았을 때 대신 쓴다.
            if (startup is null &&
                text.Contains("Configure(IApplicationBuilder", StringComparison.Ordinal))
            {
                startup = document;
            }
        }

        return startup is null ? null : new HostDocument(startup, project.AssemblyName, "startup");
    }
}

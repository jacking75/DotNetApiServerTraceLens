using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace TraceLens.Analyzer;

public sealed class SolutionLoader(ILogger<SolutionLoader> logger)
{
    private static readonly object RegistrationLock = new();

    /// <summary>분석 대상 경로(.sln 또는 .csproj)를 열어 Roslyn 워크스페이스를 만든다.</summary>
    /// <remarks>.csproj를 열면 그 프로젝트가 참조하는 프로젝트까지 함께 로드된다.</remarks>
    public async Task<LoadedSolution> LoadAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(targetPath);
        var extension = Path.GetExtension(fullPath);
        var isProject = string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase);
        var isSolution = string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase);
        if (!File.Exists(fullPath) || (!isSolution && !isProject))
        {
            throw new FileNotFoundException("분석할 .sln 또는 .csproj 파일을 찾을 수 없다.", fullPath);
        }

        EnsureMsBuildRegistered();
        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, args) => logger.LogWarning("MSBuildWorkspace: {Message}", args.Diagnostic.Message);
        var solution = isProject
            ? (await workspace.OpenProjectAsync(fullPath, cancellationToken: cancellationToken)).Solution
            : await workspace.OpenSolutionAsync(fullPath, cancellationToken: cancellationToken);

        var successfulProjects = new HashSet<ProjectId>();
        foreach (var project in solution.Projects.Where(project => project.SupportsCompilation))
        {
            try
            {
                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation is null)
                {
                    logger.LogWarning("프로젝트 {ProjectName} 컴파일을 만들지 못해 제외한다.", project.Name);
                    continue;
                }

                var errors = compilation.GetDiagnostics(cancellationToken)
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Take(5)
                    .ToArray();
                if (errors.Length > 0)
                {
                    logger.LogWarning(
                        "프로젝트 {ProjectName}에 컴파일 오류가 있어 분석은 계속하되 경고를 남긴다: {Errors}",
                        project.Name,
                        string.Join(" | ", errors.Select(error => error.GetMessage())));
                }

                successfulProjects.Add(project.Id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "프로젝트 {ProjectName} 컴파일 확인에 실패해 제외한다.", project.Name);
            }
        }

        return new LoadedSolution(workspace, solution, successfulProjects);
    }

    private static void EnsureMsBuildRegistered()
    {
        lock (RegistrationLock)
        {
            if (!MSBuildLocator.IsRegistered)
            {
                var instance = MSBuildLocator.QueryVisualStudioInstances()
                    .OrderByDescending(candidate => candidate.Version)
                    .FirstOrDefault();
                if (instance is not null)
                {
                    MSBuildLocator.RegisterInstance(instance);
                    return;
                }

                var runtimeDirectory = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
                var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent?.FullName
                    ?? throw new InvalidOperationException(".NET 설치 루트를 확인할 수 없다.");
                var sdkDirectory = Directory.EnumerateDirectories(Path.Combine(dotnetRoot, "sdk"))
                    .Select(path => new { Path = path, Version = ParseSdkVersion(Path.GetFileName(path)) })
                    .Where(item => item.Version is not null)
                    .OrderByDescending(item => item.Version)
                    .Select(item => item.Path)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException("사용 가능한 .NET SDK를 찾을 수 없다.");
                MSBuildLocator.RegisterMSBuildPath(sdkDirectory);
            }
        }
    }

    private static Version? ParseSdkVersion(string value)
    {
        var stablePart = value.Split('-', 2)[0];
        return Version.TryParse(stablePart, out var version) ? version : null;
    }
}

public sealed class LoadedSolution(
    MSBuildWorkspace workspace,
    Solution solution,
    IReadOnlySet<ProjectId> analyzableProjectIds) : IAsyncDisposable
{
    public Solution Solution { get; } = solution;
    public IReadOnlySet<ProjectId> AnalyzableProjectIds { get; } = analyzableProjectIds;

    public ValueTask DisposeAsync()
    {
        workspace.Dispose();
        return ValueTask.CompletedTask;
    }
}

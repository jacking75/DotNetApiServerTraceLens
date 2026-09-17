using System.Collections.Concurrent;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

public sealed class SolutionLoader(ILogger<SolutionLoader> logger)
{
    private static readonly object RegistrationLock = new();

    /// <summary>지금 로더가 여는 분석 대상 확장자.</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = [".sln", ".csproj"];

    /// <summary>
    /// 아직 지원하지 않는 확장자와 그 이유. 확장자 검사만 풀고 "지원 완료"라고 하지 않기 위해 이유를 함께 둔다.
    /// </summary>
    public static IReadOnlyDictionary<string, string> UnsupportedExtensions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".slnx"] = "지금 쓰는 MSBuildWorkspace(Roslyn 4.11)가 .slnx를 열지 못한다. " +
                        "같은 폴더의 .sln을 고르거나 분석할 .csproj를 직접 고르면 된다.",
            [".vbproj"] = "TraceLens는 C# 프로젝트만 분석한다.",
            [".fsproj"] = "TraceLens는 C# 프로젝트만 분석한다."
        };

    /// <summary>분석 대상 경로(.sln 또는 .csproj)를 열어 Roslyn 워크스페이스를 만든다.</summary>
    /// <remarks>.csproj를 열면 그 프로젝트가 참조하는 프로젝트까지 함께 로드된다.</remarks>
    public async Task<LoadedSolution> LoadAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(targetPath);
        var extension = Path.GetExtension(fullPath);
        if (UnsupportedExtensions.TryGetValue(extension, out var reason))
        {
            throw new NotSupportedException($"{extension} 형식은 아직 지원하지 않는다. {reason}");
        }

        var isProject = string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase);
        var isSolution = string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase);
        if (!File.Exists(fullPath) || (!isSolution && !isProject))
        {
            throw new FileNotFoundException("분석할 .sln 또는 .csproj 파일을 찾을 수 없다.", fullPath);
        }

        EnsureMsBuildRegistered();
        var workspace = MSBuildWorkspace.Create();
        var workspaceFailures = new ConcurrentBag<string>();
        workspace.WorkspaceFailed += (_, args) =>
        {
            workspaceFailures.Add(args.Diagnostic.Message);
            logger.LogWarning("MSBuildWorkspace: {Message}", args.Diagnostic.Message);
        };
        var solution = isProject
            ? (await workspace.OpenProjectAsync(fullPath, cancellationToken: cancellationToken)).Solution
            : await workspace.OpenSolutionAsync(fullPath, cancellationToken: cancellationToken);

        var successfulProjects = new HashSet<ProjectId>();
        var projectDiagnostics = new List<AnalyzedProject>();
        foreach (var project in solution.Projects.Where(project => project.SupportsCompilation))
        {
            try
            {
                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation is null)
                {
                    logger.LogWarning("프로젝트 {ProjectName} 컴파일을 만들지 못해 제외한다.", project.Name);
                    projectDiagnostics.Add(new AnalyzedProject(
                        project.Name, project.FilePath ?? string.Empty, false, 0, ["컴파일을 만들지 못했다."]));
                    continue;
                }

                var allErrors = compilation.GetDiagnostics(cancellationToken)
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .ToArray();
                var errors = allErrors.Take(5).ToArray();
                if (errors.Length > 0)
                {
                    logger.LogWarning(
                        "프로젝트 {ProjectName}에 컴파일 오류가 있어 분석은 계속하되 경고를 남긴다: {Errors}",
                        project.Name,
                        string.Join(" | ", errors.Select(error => error.GetMessage())));
                }

                projectDiagnostics.Add(new AnalyzedProject(
                    project.Name,
                    project.FilePath ?? string.Empty,
                    true,
                    allErrors.Length,
                    errors.Select(error => error.GetMessage()).ToArray()));
                successfulProjects.Add(project.Id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "프로젝트 {ProjectName} 컴파일 확인에 실패해 제외한다.", project.Name);
                projectDiagnostics.Add(new AnalyzedProject(
                    project.Name, project.FilePath ?? string.Empty, false, 0, [exception.Message]));
            }
        }

        return new LoadedSolution(workspace, solution, successfulProjects)
        {
            ProjectDiagnostics = projectDiagnostics,
            WorkspaceFailures = workspaceFailures.Distinct(StringComparer.Ordinal).Take(20).ToArray()
        };
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

                MSBuildLocator.RegisterMSBuildPath(FindLatestSdkDirectory());
            }
        }
    }

    /// <summary>등록할 MSBuild(=SDK) 디렉터리를 찾는다. doctor 진단도 같은 규칙을 쓴다.</summary>
    public static string FindLatestSdkDirectory()
    {
        var runtimeDirectory = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException(".NET 설치 루트를 확인할 수 없다.");
        var sdkRoot = Path.Combine(dotnetRoot, "sdk");
        if (!Directory.Exists(sdkRoot))
        {
            throw new InvalidOperationException($"사용 가능한 .NET SDK를 찾을 수 없다: {sdkRoot}");
        }

        return Directory.EnumerateDirectories(sdkRoot)
                   .Select(path => new { Path = path, Version = ParseSdkVersion(Path.GetFileName(path)) })
                   .Where(item => item.Version is not null)
                   .OrderByDescending(item => item.Version)
                   .Select(item => item.Path)
                   .FirstOrDefault()
               ?? throw new InvalidOperationException("사용 가능한 .NET SDK를 찾을 수 없다.");
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

    /// <summary>프로젝트별 로드·컴파일 결과. UI와 AI 문맥에 "무엇을 못 봤는지" 알리는 근거다.</summary>
    public IReadOnlyList<AnalyzedProject> ProjectDiagnostics { get; init; } = [];

    /// <summary>MSBuildWorkspace가 남긴 경고. 프로젝트 로드 실패의 실제 이유가 여기에 있는 경우가 많다.</summary>
    public IReadOnlyList<string> WorkspaceFailures { get; init; } = [];

    private readonly ConcurrentDictionary<DocumentId, DocumentSemantics?> semanticCache = new();

    /// <summary>미리 만든 시맨틱 모델 수. 0이면 준비 단계를 건너뛰었다는 뜻이다(문서가 너무 많은 경우).</summary>
    public int PrewarmedDocuments { get; private set; }

    /// <summary>
    /// 문서의 구문 트리와 시맨틱 모델을 한 번만 만들어 모든 수집기가 나눠 쓴다.
    /// Roslyn이 들고 있는 캐시는 약한 참조라, 수집기 다섯 개가 차례로 돌면 같은 문서를 여러 번 바인딩하곤 한다.
    /// </summary>
    public async ValueTask<DocumentSemantics?> GetSemanticsAsync(Document document, CancellationToken cancellationToken)
    {
        if (semanticCache.TryGetValue(document.Id, out var cached))
        {
            return cached;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var model = await document.GetSemanticModelAsync(cancellationToken);
        var created = root is null || model is null ? null : new DocumentSemantics(root, model);
        semanticCache[document.Id] = created;
        return created;
    }

    /// <summary>
    /// 분석할 문서의 구문 트리·시맨틱 모델을 미리 병렬로 만든다.
    /// 문서가 <paramref name="maxDocuments"/>개를 넘으면 모델을 전부 들고 있는 메모리가 부담이라 건너뛴다.
    /// 건너뛰어도 결과는 같다. 수집기마다 예전처럼 그때그때 만든다.
    /// </summary>
    public async Task PrewarmSemanticsAsync(int maxDocuments, CancellationToken cancellationToken = default)
    {
        var documents = Solution.Projects
            .Where(project => AnalyzableProjectIds.Contains(project.Id))
            .SelectMany(project => project.Documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular))
            .ToArray();
        if (documents.Length == 0 || documents.Length > maxDocuments)
        {
            return;
        }

        await Parallel.ForEachAsync(
            documents,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (document, token) => await GetSemanticsAsync(document, token));
        PrewarmedDocuments = documents.Length;
    }

    public ValueTask DisposeAsync()
    {
        workspace.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>문서 하나의 구문 트리와 시맨틱 모델. 수집기 여러 개가 같은 문서를 다시 바인딩하지 않게 묶어 둔다.</summary>
public sealed record DocumentSemantics(SyntaxNode Root, SemanticModel Model);

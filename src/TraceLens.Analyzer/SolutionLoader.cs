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
        var exclusions = await TraceLensConfig.ReadProjectExclusionsAsync(
            Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? Directory.GetCurrentDirectory(),
            cancellationToken);
        return await LoadAsync(targetPath, exclusions, cancellationToken);
    }

    /// <param name="excludeProjectNamePatterns">
    /// 로드에서 뺄 프로젝트 이름 패턴(와일드카드 <c>*</c>). 뺀 프로젝트는 컴파일하지 않고
    /// 진단에 <c>Loaded=false</c>로 남긴다 — 조용히 사라지면 "왜 이 타입이 없지"를 알 수 없다.
    /// </param>
    public async Task<LoadedSolution> LoadAsync(
        string targetPath,
        IReadOnlyList<string> excludeProjectNamePatterns,
        CancellationToken cancellationToken = default)
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
        var testProjectIds = new HashSet<ProjectId>();
        var projectDiagnostics = new List<AnalyzedProject>();
        foreach (var project in solution.Projects.Where(project => project.SupportsCompilation))
        {
            if (excludeProjectNamePatterns.Count > 0 &&
                PatternMatch.MatchesAny(project.Name, excludeProjectNamePatterns))
            {
                logger.LogInformation("excludeProjectNamePatterns에 맞아 {ProjectName}을(를) 로드에서 뺐다.", project.Name);
                projectDiagnostics.Add(new AnalyzedProject(
                    project.Name, project.FilePath ?? string.Empty, false, 0,
                    ["excludeProjectNamePatterns 설정으로 제외했다."]));
                continue;
            }

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

                var isTest = IsTestProject(project);
                if (isTest)
                {
                    testProjectIds.Add(project.Id);
                }

                projectDiagnostics.Add(new AnalyzedProject(
                    project.Name,
                    project.FilePath ?? string.Empty,
                    true,
                    allErrors.Length,
                    errors.Select(error => error.GetMessage()).ToArray()) { IsTest = isTest });
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
            TestProjectIds = testProjectIds,
            WorkspaceFailures = workspaceFailures.Distinct(StringComparer.Ordinal).Take(20).ToArray()
        };
    }

    /// <summary>
    /// 테스트 프로젝트인지. 테스트 프레임워크 참조나 이름 규칙으로 판정한다.
    /// MSBuild의 <c>IsTestProject</c> 속성은 Roslyn 워크스페이스에 그대로 오지 않아 참조를 본다.
    /// </summary>
    private static bool IsTestProject(Project project)
    {
        foreach (var reference in project.MetadataReferences)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(reference.Display ?? string.Empty);
            if (name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("nunit", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return project.Name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
               project.Name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
               project.Name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase);
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
                    RegisteredMsBuildDescription = $"{instance.Name} {instance.Version} ({instance.MSBuildPath})";
                    return;
                }

                var sdkDirectory = FindLatestSdkDirectory();
                MSBuildLocator.RegisterMSBuildPath(sdkDirectory);
                RegisteredMsBuildDescription = $".NET SDK ({sdkDirectory})";
            }
        }
    }

    /// <summary>
    /// 분석에 필요한 최소 SDK 메이저 버전. 이보다 낮으면 net10.0 프로젝트를 열지 못한다.
    /// doctor가 <c>SDK_TOO_OLD</c>를 판정할 때 쓴다.
    /// </summary>
    public const int MinimumSdkMajor = 10;

    /// <summary>
    /// 마지막으로 등록한 MSBuild 인스턴스 설명(이름·버전·경로).
    /// 어느 MSBuild를 골랐는지 doctor가 말할 수 있어야 "분석은 되는데 결과가 다르다"를 추적할 수 있다.
    /// </summary>
    public static string? RegisteredMsBuildDescription { get; private set; }

    /// <summary>폴더 이름에서 SDK 버전을 읽는다. doctor가 설치된 SDK 목록을 만들 때 함께 쓴다.</summary>
    public static Version? ReadSdkVersion(string directoryName) => ParseSdkVersion(directoryName);

    /// <summary>설치된 SDK 디렉터리 전부(버전 내림차순). 하나도 없으면 빈 목록이다.</summary>
    public static IReadOnlyList<string> ListSdkDirectories()
    {
        try
        {
            var runtimeDirectory = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
            var sdkRoot = Path.Combine(runtimeDirectory.Parent?.Parent?.Parent?.FullName ?? string.Empty, "sdk");
            if (!Directory.Exists(sdkRoot))
            {
                return [];
            }

            return Directory.EnumerateDirectories(sdkRoot)
                .Select(path => new { Path = path, Version = ParseSdkVersion(Path.GetFileName(path)) })
                .Where(item => item.Version is not null)
                .OrderByDescending(item => item.Version)
                .Select(item => item.Path)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
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

    /// <summary>테스트 프로젝트의 ID. 유일 구현체 판정에서 이 프로젝트의 타입을 후보에서 뺀다. </summary>
    public IReadOnlySet<ProjectId> TestProjectIds { get; init; } = new HashSet<ProjectId>();

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

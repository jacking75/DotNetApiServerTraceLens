using Microsoft.CodeAnalysis;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 이 분석이 무엇을 읽었는지(<see cref="AnalysisManifest"/>)와 분석 대상 소스 파일 목록을 만든다.
/// 신선도 확인이 "기록한 파일의 해시"만 보면 새로 생긴 <c>.cs</c>를 놓치므로,
/// 프로젝트 디렉터리(=소스 루트)도 함께 기록해서 파일 집합 비교가 가능하게 한다.
/// </summary>
public sealed class AnalysisManifestBuilder
{
    /// <summary>소스 루트를 훑을 때 건너뛰는 디렉터리.</summary>
    public static IReadOnlyList<string> ExcludedDirectories { get; } =
        ["bin", "obj", ".git", ".tracelens", ".vs", "node_modules"];

    /// <summary>분석 결과에 영향을 주는 설정 파일. 프로젝트 디렉터리에서 분석 루트까지 올라가며 찾는다.</summary>
    private static readonly (string FileName, ManifestInputKind Kind)[] ConfigFiles =
    [
        ("Directory.Build.props", ManifestInputKind.BuildProps),
        ("Directory.Build.targets", ManifestInputKind.BuildProps),
        ("Directory.Packages.props", ManifestInputKind.PackagesProps),
        ("global.json", ManifestInputKind.GlobalJson)
    ];

    public async Task<AnalysisManifestResult> BuildAsync(
        LoadedSolution loaded,
        string targetPath,
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        var sources = new SortedSet<string>(StringComparer.Ordinal);
        var linked = new SortedSet<string>(StringComparer.Ordinal);
        var sourceRoots = new SortedSet<string>(StringComparer.Ordinal);
        var inputs = new Dictionary<string, ManifestInput>(StringComparer.OrdinalIgnoreCase);
        var limitations = new List<string>();

        var extension = Path.GetExtension(targetPath);
        var targetKind = string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase)
            ? ManifestInputKind.Solution
            : ManifestInputKind.Project;
        await AddInputAsync(inputs, rootDirectory, targetPath, targetKind, cancellationToken);

        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(project.FilePath))
            {
                await AddInputAsync(inputs, rootDirectory, project.FilePath, ManifestInputKind.Project, cancellationToken);
                var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(project.FilePath));
                if (projectDirectory is not null)
                {
                    var relativeRoot = SourcePaths.ToSolutionRelative(rootDirectory, projectDirectory);
                    if (relativeRoot is not null)
                    {
                        sourceRoots.Add(relativeRoot);
                    }

                    await AddConfigFilesAsync(inputs, rootDirectory, projectDirectory, cancellationToken);
                }
            }

            foreach (var document in project.Documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular))
            {
                if (string.IsNullOrEmpty(document.FilePath))
                {
                    continue;
                }

                var relativePath = SourcePaths.ToSolutionRelative(rootDirectory, document.FilePath);
                if (relativePath is null)
                {
                    linked.Add(Path.GetFullPath(document.FilePath));
                    continue;
                }

                if (!SourcePaths.IsBuildOutput(relativePath))
                {
                    sources.Add(relativePath);
                }
            }
        }

        var configPath = Path.Combine(rootDirectory, "tracelens.config.json");
        if (File.Exists(configPath))
        {
            await AddInputAsync(inputs, rootDirectory, configPath, ManifestInputKind.Config, cancellationToken);
        }

        limitations.Add("MSBuild import 전개 결과와 analyzer·source generator가 만든 소스는 입력 목록에 넣지 않는다.");
        if (linked.Count > 0)
        {
            limitations.Add($"분석 루트 밖에서 링크된 소스가 {linked.Count}개 있다. 이 파일은 새 파일 검사 대상이 아니다.");
        }

        if (loaded.WorkspaceFailures.Count > 0)
        {
            limitations.Add($"MSBuild 워크스페이스 경고가 {loaded.WorkspaceFailures.Count}건 있다. 일부 프로젝트가 불완전하게 로드됐을 수 있다.");
        }

        var draft = new AnalysisManifest(
            inputs.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray(),
            sourceRoots.ToArray(),
            ExcludedDirectories,
            linked.ToArray(),
            limitations);

        // 소스 루트 아래에 있지만 어떤 프로젝트도 컴파일하지 않는 .cs를 기록한다(<Compile Remove> 등).
        // 이 목록이 없으면 신선도 검사가 그 파일을 "새로 생긴 파일"로 보고 영원히 stale이 된다.
        var ignored = EnumerateSourceFiles(rootDirectory, draft)
            .Where(file => !sources.Contains(file))
            .ToArray();
        if (ignored.Length > 0)
        {
            limitations.Add(
                $"소스 루트 아래에 있지만 어떤 프로젝트도 컴파일하지 않는 .cs가 {ignored.Length}개 있다" +
                "(<Compile Remove>·조건부 컴파일). 이 파일은 분석에도 신선도 검사에도 들어가지 않는다.");
        }

        var manifest = draft with { IgnoredSources = ignored };
        return new AnalysisManifestResult(manifest, sources.ToArray());
    }

    private static async Task AddConfigFilesAsync(
        Dictionary<string, ManifestInput> inputs,
        string rootDirectory,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var rootFull = Path.GetFullPath(rootDirectory);
        var current = new DirectoryInfo(projectDirectory);
        while (current is not null)
        {
            foreach (var (fileName, kind) in ConfigFiles)
            {
                var path = Path.Combine(current.FullName, fileName);
                if (File.Exists(path))
                {
                    await AddInputAsync(inputs, rootDirectory, path, kind, cancellationToken);
                }
            }

            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), rootFull.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = current.Parent;
        }
    }

    private static async Task AddInputAsync(
        Dictionary<string, ManifestInput> inputs,
        string rootDirectory,
        string path,
        ManifestInputKind kind,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (inputs.ContainsKey(fullPath))
        {
            return;
        }

        var relativePath = SourcePaths.ToSolutionRelative(rootDirectory, fullPath) ?? fullPath.Replace('\\', '/');
        inputs[fullPath] = new ManifestInput(
            relativePath,
            await SnapshotHasher.ComputeFileAsync(fullPath, cancellationToken),
            kind);
    }

    /// <summary>소스 루트 아래에 실제로 존재하는 <c>.cs</c> 파일의 상대 경로를 모은다. 신선도의 "새 파일" 검사에 쓴다.</summary>
    public static IReadOnlyList<string> EnumerateSourceFiles(string rootDirectory, AnalysisManifest manifest)
    {
        var excluded = manifest.ExcludedDirectories.Count > 0 ? manifest.ExcludedDirectories : ExcludedDirectories;
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var sourceRoot in manifest.SourceRoots)
        {
            var directory = Path.Combine(rootDirectory, sourceRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in EnumerateCSharpFiles(directory, excluded))
            {
                var relativePath = SourcePaths.ToSolutionRelative(rootDirectory, file);
                if (relativePath is not null)
                {
                    result.Add(relativePath);
                }
            }
        }

        return result.ToArray();
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string directory, IReadOnlyList<string> excluded)
    {
        IEnumerable<string> files;
        IEnumerable<string> subdirectories;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
            subdirectories = Directory.EnumerateDirectories(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return file;
        }

        foreach (var subdirectory in subdirectories)
        {
            var name = Path.GetFileName(subdirectory);
            if (excluded.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var file in EnumerateCSharpFiles(subdirectory, excluded))
            {
                yield return file;
            }
        }
    }
}

/// <param name="SourceFiles">분석에 실제로 들어간 <c>.cs</c>의 분석 루트 기준 상대 경로.</param>
public sealed record AnalysisManifestResult(AnalysisManifest Manifest, IReadOnlyList<string> SourceFiles);

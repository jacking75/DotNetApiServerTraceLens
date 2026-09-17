using System.Text.Json;

namespace TraceLens.Context;

/// <param name="RelativeTargetPath">복사한 폴더 기준으로 분석할 대상(.sln 또는 .csproj)의 상대 경로.</param>
/// <param name="Expected">이 예제를 분석하면 무엇이 보이는지. 사용자가 결과를 스스로 확인할 수 있게 적어 둔다.</param>
public sealed record DemoSample(
    string Id,
    string Title,
    string Description,
    string SourceDirectory,
    string RelativeTargetPath,
    IReadOnlyList<string> Expected,
    IReadOnlyList<string> LearningSteps);

/// <param name="Destination">복사한 폴더의 전체 경로.</param>
/// <param name="CopiedDirectories">실제로 복사한 소스 폴더. 같은 폴더를 가리키는 예제가 여럿이면 한 번만 들어간다.</param>
public sealed record SampleCopyResult(string Destination, IReadOnlyList<string> CopiedDirectories);

/// <param name="TargetPath">복사한 뒤 분석할 대상의 전체 경로.</param>
/// <param name="Copied">이번 호출에서 실제로 복사했는지. false면 이미 있던 것을 그대로 쓴다.</param>
public sealed record SamplePreparation(string TargetPath, bool Copied);

/// <summary>
/// 배포 패키지에 함께 들어가는 예제를 찾아 사용자가 고른 폴더로 복사한다.
/// 예제는 "즉시 탐색"이 아니라 <b>직접 분석</b>용이다. 복사한 뒤 사용자가 명시적으로 분석을 실행한다.
/// </summary>
public static class DemoSamples
{
    private static readonly string[] MetadataFileNames = ["samples.json"];

    public static IReadOnlyList<DemoSample> Find()
    {
        foreach (var root in CandidateRoots())
        {
            foreach (var fileName in MetadataFileNames)
            {
                var metadataPath = Path.Combine(root, fileName);
                if (!File.Exists(metadataPath))
                {
                    continue;
                }

                try
                {
                    var samples = JsonSerializer.Deserialize<DemoSample[]>(
                        File.ReadAllText(metadataPath),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (samples is { Length: > 0 })
                    {
                        return samples
                            .Select(sample => sample with { SourceDirectory = Path.Combine(root, sample.SourceDirectory) })
                            .Where(sample => Directory.Exists(sample.SourceDirectory))
                            .ToArray();
                    }
                }
                catch (JsonException)
                {
                    // 예제 목록이 깨져 있으면 없는 것으로 본다.
                }
            }
        }

        return [];
    }

    /// <summary>
    /// 예제 하나를 사용자 영역으로 복사하고 분석 대상 경로를 돌려준다.
    /// 도구 설치 폴더 안에서 직접 분석하지 않는 이유는 쓰기가 막혀 있을 수 있고 <c>.tracelens</c> 산출물이 생기기 때문이다.
    /// 이미 복사돼 있으면 다시 복사하지 않는다(<paramref name="overwrite"/>가 true일 때만 덮어쓴다).
    /// </summary>
    public static Task<SamplePreparation> PrepareAsync(DemoSample sample, string destination, bool overwrite)
    {
        var targetPath = Path.GetFullPath(Path.Combine(destination, sample.RelativeTargetPath));
        if (!overwrite && File.Exists(targetPath))
        {
            return Task.FromResult(new SamplePreparation(targetPath, false));
        }

        Directory.CreateDirectory(destination);
        CopyDirectory(sample.SourceDirectory, Path.Combine(destination, Path.GetFileName(sample.SourceDirectory)));
        return Task.FromResult(new SamplePreparation(targetPath, true));
    }

    /// <summary>예제를 복사할 기본 위치. 사용자 영역이라 쓰기 권한 문제가 없다.</summary>
    public static string DefaultDestination(string sampleId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TraceLens",
        "samples",
        sampleId);

    /// <summary>
    /// 예제 목록을 한 폴더로 복사한다.
    /// 여러 예제가 같은 소스 폴더를 가리키면(학습 경로가 둘인 한 프로젝트) 한 번만 복사한다.
    /// </summary>
    public static Task<SampleCopyResult> CopyAsync(IReadOnlyList<DemoSample> samples, string destination)
    {
        Directory.CreateDirectory(destination);
        var copied = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)
        {
            if (!seen.Add(sample.SourceDirectory))
            {
                continue;
            }

            CopyDirectory(sample.SourceDirectory, Path.Combine(destination, Path.GetFileName(sample.SourceDirectory)));
            copied.Add(sample.SourceDirectory);
        }

        return Task.FromResult(new SampleCopyResult(Path.GetFullPath(destination), copied));
    }

    /// <summary>
    /// 예제 폴더를 찾아볼 위치. 배포물은 실행 파일 옆에 <c>samples</c>가 있고,
    /// 소스 빌드(<c>run.ps1</c>)는 <c>src/TraceLens.Server/bin/…</c>에서 도니 저장소 루트까지 거슬러 올라가며 찾는다.
    /// </summary>
    private static IEnumerable<string> CandidateRoots()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = SafeDirectory(start);
            for (var level = 0; current is not null && level <= MaxAncestorLevels; level++, current = current.Parent)
            {
                yield return Path.Combine(current.FullName, "samples");
            }
        }
    }

    /// <summary>저장소 루트까지는 보통 6~7단계면 닿는다. 드라이브 루트까지 훑지 않도록 상한을 둔다.</summary>
    private const int MaxAncestorLevels = 7;

    private static DirectoryInfo? SafeDirectory(string path)
    {
        try
        {
            return new DirectoryInfo(path);
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is "bin" or "obj" or ".tracelens")
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }
}

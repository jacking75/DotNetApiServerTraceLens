using System.Text.Json;

namespace TraceLens.Cli;

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

    public static Task<string> CopyAsync(IReadOnlyList<DemoSample> samples, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var sample in samples)
        {
            CopyDirectory(sample.SourceDirectory, Path.Combine(destination, Path.GetFileName(sample.SourceDirectory)));
        }

        return Task.FromResult(Path.GetFullPath(destination));
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "samples");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "samples");
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "samples");
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

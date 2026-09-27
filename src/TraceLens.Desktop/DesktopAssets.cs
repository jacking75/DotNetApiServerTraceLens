using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace TraceLens.Desktop;

/// <summary>단일 EXE의 웹 자산을 버전별 사용자 캐시에 풀어 기존 서버의 물리 파일 경로로 제공한다.</summary>
internal static class DesktopAssets
{
    private const string Prefix = "desktop-assets/";

    public static string Extract()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.Replace('\\', '/').StartsWith(Prefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!names.Any(name => name.Replace('\\', '/') == Prefix + "wwwroot/index.html"))
        {
            throw new InvalidOperationException("내장 웹 화면을 찾지 못했다. 먼저 클라이언트를 빌드해야 한다.");
        }

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in names)
        {
            digest.AppendData(Encoding.UTF8.GetBytes(name));
            using var stream = assembly.GetManifestResourceStream(name)!;
            var buffer = new byte[64 * 1024];
            int count;
            while ((count = stream.Read(buffer)) > 0) digest.AppendData(buffer.AsSpan(0, count));
        }

        var version = Convert.ToHexString(digest.GetHashAndReset())[..16].ToLowerInvariant();
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TraceLens", "desktop-assets", version);
        using var mutex = new Mutex(false, $"Local\\TraceLens.DesktopAssets.{version}");
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { /* 이전 실행이 추출 중 종료됐다. 아래에서 다시 채운다. */ }
        try
        {
            var ready = Path.Combine(root, ".ready");
            if (File.Exists(ready)) return root;

            Directory.CreateDirectory(root);
            foreach (var name in names)
            {
                var relative = name.Replace('\\', '/')[Prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                var destination = Path.GetFullPath(Path.Combine(root, relative));
                if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"잘못된 내장 자산 경로다: {name}");

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = assembly.GetManifestResourceStream(name)!;
                using var target = File.Create(destination);
                source.CopyTo(target);
            }
            File.WriteAllText(ready, version);
            return root;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}

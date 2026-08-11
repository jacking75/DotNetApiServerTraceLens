using System.Text.Json;
using TraceLens.Analyzer.Models;

namespace TraceLens.Mcp;

public sealed class GraphFileStore(string graphPath)
{
    public string GraphPath { get; } = Path.GetFullPath(graphPath);

    public async Task<GraphDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(GraphPath))
        {
            throw new FileNotFoundException("TraceLens 그래프 파일을 찾을 수 없다.", GraphPath);
        }

        await using var stream = File.OpenRead(GraphPath);
        return await JsonSerializer.DeserializeAsync<GraphDocument>(
                   stream,
                   GraphDocument.JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException("TraceLens 그래프 파일을 읽을 수 없다.");
    }

    public async Task<SourceResult> ReadSourceAsync(
        GraphDocument graph,
        string file,
        int startLine,
        int endLine,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file) || startLine < 1 || endLine < startLine)
        {
            throw new ArgumentException("file, startLine, endLine 값을 확인해야 한다.");
        }

        var solutionDirectory = Path.GetDirectoryName(graph.SolutionPath)
            ?? throw new InvalidDataException("그래프의 solutionPath가 올바르지 않다.");
        var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, file));
        var relativePath = Path.GetRelativePath(solutionDirectory, fullPath);
        if (Path.IsPathRooted(file) || relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("솔루션 외부 파일은 읽을 수 없다.", nameof(file));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("소스 파일을 찾을 수 없다.", fullPath);
        }

        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        if (startLine > lines.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine), "시작 라인이 파일 범위를 벗어났다.");
        }

        var actualEndLine = Math.Min(endLine, lines.Length);
        return new SourceResult(
            relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            startLine,
            actualEndLine,
            string.Join(Environment.NewLine, lines[(startLine - 1)..actualEndLine]));
    }
}

public sealed record SourceResult(string File, int StartLine, int EndLine, string Content);

namespace TraceLens.Analyzer.Models;

public sealed record GraphClass(
    string Id,
    string Name,
    string Namespace,
    NodeKind Kind,
    string File,
    int StartLine,
    int EndLine,
    IReadOnlyList<string> MethodIds);

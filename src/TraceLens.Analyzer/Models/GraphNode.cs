namespace TraceLens.Analyzer.Models;

public sealed record GraphNode(
    string Id,
    NodeKind Kind,
    string DisplayName,
    string Namespace,
    string ClassId,
    string File,
    int StartLine,
    int EndLine);

public enum NodeKind
{
    Controller,
    Service,
    Repository,
    Validator,
    Infra,
    Db,
    Method
}

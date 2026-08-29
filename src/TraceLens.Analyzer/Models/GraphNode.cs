namespace TraceLens.Analyzer.Models;

public sealed record GraphNode(
    string Id,
    NodeKind Kind,
    string DisplayName,
    string Namespace,
    string ClassId,
    string File,
    int StartLine,
    int EndLine)
{
    private readonly IReadOnlyList<string> parameterTypes = [];

    public MemberAccessibility Accessibility { get; init; } = MemberAccessibility.Public;

    /// <summary>표시용 반환 타입. 생성자는 빈 문자열이다.</summary>
    public string ReturnType { get; init; } = string.Empty;

    /// <summary>노드 id에 쓰는 것과 같은 전체 타입 이름 목록.</summary>
    public IReadOnlyList<string> ParameterTypes
    {
        get => parameterTypes;
        init => parameterTypes = value ?? [];
    }
}

public enum NodeKind
{
    Controller,
    Service,
    Repository,
    Validator,
    Infra,
    Db,
    Method,
    Interface
}

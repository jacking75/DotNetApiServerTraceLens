namespace TraceLens.Analyzer.Models;

/// <summary>선언 종류. UML 박스의 스테레오타입(«interface», «record»)을 정하는 데 쓴다.</summary>
public enum ClassTypeKind
{
    Class,
    Record,
    Struct,
    Interface
}

/// <param name="Kind">kindRules로 정한 분류(Controller/Service/…). 색과 아이콘에 쓴다.</param>
/// <param name="MethodIds">실행 가능한 노드만 담는 기존 목록. 화면에 그릴 멤버는 <see cref="Members"/>를 쓴다.</param>
public sealed record GraphClass(
    string Id,
    string Name,
    string Namespace,
    NodeKind Kind,
    string File,
    int StartLine,
    int EndLine,
    IReadOnlyList<string> MethodIds)
{
    private readonly IReadOnlyList<string> interfaceIds = [];
    private readonly IReadOnlyList<string> interfaceNames = [];
    private readonly IReadOnlyList<GraphClassMember> members = [];

    public ClassTypeKind TypeKind { get; init; } = ClassTypeKind.Class;

    public bool IsAbstract { get; init; }

    public bool IsStatic { get; init; }

    /// <summary>상위 타입이 솔루션 안에 있으면 그 클래스 id, 아니면 null.</summary>
    public string? BaseTypeId { get; init; }

    /// <summary>표시용 상위 타입 이름. 솔루션 밖 타입(<c>ControllerBase</c> 등)도 이름은 남긴다.</summary>
    public string? BaseTypeName { get; init; }

    /// <summary>직접 구현한 인터페이스 중 솔루션 안에 있는 것. 상위 인터페이스까지 선이 겹치지 않도록 AllInterfaces를 쓰지 않는다.</summary>
    public IReadOnlyList<string> InterfaceIds
    {
        get => interfaceIds;
        init => interfaceIds = value ?? [];
    }

    public IReadOnlyList<string> InterfaceNames
    {
        get => interfaceNames;
        init => interfaceNames = value ?? [];
    }

    /// <summary>스키마 v3 이하로 저장된 그래프에는 없으므로 빈 목록으로 읽는다.</summary>
    public IReadOnlyList<GraphClassMember> Members
    {
        get => members;
        init => members = value ?? [];
    }
}

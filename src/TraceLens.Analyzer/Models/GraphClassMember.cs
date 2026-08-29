namespace TraceLens.Analyzer.Models;

/// <summary>클래스 다이어그램의 UML 박스에 그리는 멤버 한 줄.</summary>
public enum MemberKind
{
    Field,
    Property,
    Constructor,
    Method
}

/// <summary>C#의 접근 제한자를 화면에 표시할 네 가지로 줄인 값.</summary>
public enum MemberAccessibility
{
    Public,
    Protected,
    Internal,
    Private
}

/// <summary>
/// 클래스가 선언한 멤버 하나. <see cref="GraphNode"/>가 "실행 그래프"라면 이쪽은 "화면에 그리는 모델"이라,
/// 인터페이스 메서드처럼 본문이 없어 노드가 되지 못하는 멤버도 담는다. 둘을 잇는 것이 <see cref="NodeId"/>다.
/// </summary>
/// <param name="Type">표시용 타입 문자열. 메서드는 반환 타입, 생성자는 빈 문자열이다.</param>
/// <param name="TypeIds">그 타입에서 뽑아낸 솔루션 안 타입 id. 제네릭 인자와 배열 원소까지 재귀로 훑는다.</param>
/// <param name="NodeId">실행 가능한 멤버면 대응하는 <see cref="GraphNode.Id"/>, 아니면 null.</param>
public sealed record GraphClassMember(
    MemberKind MemberKind,
    string Name,
    string? Signature,
    string Type,
    IReadOnlyList<string> TypeIds,
    MemberAccessibility Accessibility,
    bool IsStatic,
    int StartLine,
    int EndLine,
    string? NodeId)
{
    private readonly IReadOnlyList<string> typeIds = TypeIds ?? [];

    public IReadOnlyList<string> TypeIds
    {
        get => typeIds;
        init => typeIds = value ?? [];
    }
}

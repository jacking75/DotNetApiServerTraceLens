namespace FixtureApp.Models;

/// <summary>생성자를 따로 두지 않은 DTO. new로 만들 때 기본 생성자 노드가 생기는지 검증한다.</summary>
public sealed class OrderDraft
{
    public int Id { get; set; }
}

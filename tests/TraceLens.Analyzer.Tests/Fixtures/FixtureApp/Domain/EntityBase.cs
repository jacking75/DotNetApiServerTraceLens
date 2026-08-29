namespace FixtureApp.Domain;

/// <summary>파생 클래스가 둘인 추상 기반 클래스. baseTypeId와 isAbstract 검증용이다.</summary>
public abstract class EntityBase
{
    public int Id { get; set; }
}

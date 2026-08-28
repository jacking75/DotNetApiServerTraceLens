namespace FixtureApp.Services;

/// <summary>어디에도 DI 등록하지 않은 인터페이스. 구현체가 하나뿐일 때의 폴백을 검증한다.</summary>
public interface IAuditService
{
    Task RecordAsync(int id);
}

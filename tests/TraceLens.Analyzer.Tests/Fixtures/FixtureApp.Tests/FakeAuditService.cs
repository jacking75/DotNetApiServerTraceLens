using FixtureApp.Services;

namespace FixtureApp.Tests;

/// <summary>
/// 테스트 더블. 이것이 구현체 후보로 세어지면 `IAuditService`의 구현체가 2개가 되어
/// 유일 구현체 추정이 꺼지고 해석률·엣지가 조용히 줄어든다(T59).
/// </summary>
public sealed class FakeAuditService : IAuditService
{
    public Task RecordAsync(int id) => Task.CompletedTask;
}

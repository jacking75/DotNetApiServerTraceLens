using FixtureApp.Repositories;

namespace FixtureApp.Jobs;

/// <summary>어떤 HTTP 진입점에서도 도달하지 못하는 클래스. 클래스 메뉴/솔루션 전체 스캔 검증용이다.</summary>
public sealed class CleanupJob(OrderRepository repository)
{
    public Task RunAsync(int id) => repository.SaveAsync(id);
}

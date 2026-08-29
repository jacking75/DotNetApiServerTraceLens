using FixtureApp.Domain;
using FixtureApp.Repositories;

namespace FixtureApp.Services;

/// <summary>필드로 인터페이스를 주입받는 서비스. 멤버 typeIds가 인터페이스 id로 이어지는지 검증한다.</summary>
public sealed class ReportService
{
    private readonly IOrderRepository repository;

    public ReportService(IOrderRepository repository) => this.repository = repository;

    public Task<Order?> FindAsync(int id) => Task.FromResult<Order?>(null);
}

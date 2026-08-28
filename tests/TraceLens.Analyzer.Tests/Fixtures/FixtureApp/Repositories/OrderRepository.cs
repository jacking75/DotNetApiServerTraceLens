using FixtureApp.Models;

namespace FixtureApp.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    private readonly OrderDraft draft;

    /// <summary>생성자 본문 안의 호출도 그래프에 들어가는지 검증한다.</summary>
    public OrderRepository()
    {
        draft = CreateDraft();
    }

    /// <summary>본문이 있는 프로퍼티. 접근자 안의 호출이 잡히는지 검증한다.</summary>
    public int PendingCount => CountPending();

    public Task SaveAsync(int id) => Task.CompletedTask;

    private static OrderDraft CreateDraft() => new();

    private int CountPending() => draft.Id;
}

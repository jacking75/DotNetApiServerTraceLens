using FixtureApp.Models;
using FixtureApp.Repositories;

namespace FixtureApp.Services;

public sealed class OrderService(IOrderRepository repository) : IOrderService
{
    public Task CreateAsync(int id)
    {
        var draft = new OrderDraft { Id = id };
        return repository.SaveAsync(draft.Id);
    }
}

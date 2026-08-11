using FixtureApp.Repositories;

namespace FixtureApp.Services;

public sealed class OrderService(IOrderRepository repository) : IOrderService
{
    public Task CreateAsync(int id) => repository.SaveAsync(id);
}

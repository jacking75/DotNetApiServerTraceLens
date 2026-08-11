namespace FixtureApp.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    public Task SaveAsync(int id) => Task.CompletedTask;
}

namespace FixtureApp.Repositories;

public interface IOrderRepository
{
    Task SaveAsync(int id);
}

namespace FixtureApp.Repositories;

/// <summary>
/// 제네릭 저장소. 호출 지점은 <c>Repository&lt;Order&gt;.GetAsync</c>(구성된 심볼)이고
/// 선언은 <c>Repository&lt;T&gt;.GetAsync</c>다. 두 ID가 다르면 엣지가 버려진다.
/// </summary>
public class Repository<T>
    where T : class, new()
{
    public Task<T?> GetAsync(int id) => Task.FromResult<T?>(new T());

    public Task SaveAsync(T item) => Task.CompletedTask;
}

public sealed class OrderRow
{
    public int Id { get; set; }
}

/// <summary>확장 메서드. 호출 지점은 축약형이라 수신자가 인자로 바뀐다.</summary>
public static class RepositoryExtensions
{
    public static Task<OrderRow?> GetOrDefaultAsync(this Repository<OrderRow> repository, int id) =>
        repository.GetAsync(id);
}

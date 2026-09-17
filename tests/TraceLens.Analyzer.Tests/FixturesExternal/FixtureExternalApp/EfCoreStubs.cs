// EF Core 패키지를 참조하지 않고 같은 네임스페이스·타입 이름만 흉내 낸다.
// tracelens.config.json의 excludeNamespacePrefixes로 이 네임스페이스를 솔루션 밖처럼 다루게 해서
// "인식은 했지만 해석하지 않은 입력"이 집계되는지 확인한다.
namespace Microsoft.EntityFrameworkCore;

public class DbSet<TEntity> where TEntity : class
{
    public void Add(TEntity entity) { }

    public void Remove(TEntity entity) { }
}

public class DbContext
{
    public DbSet<TEntity> Set<TEntity>() where TEntity : class => new();

    public int SaveChanges() => 0;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}

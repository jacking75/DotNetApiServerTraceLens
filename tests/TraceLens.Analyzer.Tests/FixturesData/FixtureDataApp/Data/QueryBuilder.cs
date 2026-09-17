// SqlKata 패키지를 넣지 않고 전체 이름만 맞춘 스텁이다.
// 분석기는 설정의 root·queryType·terminal 이름으로만 매칭하므로 스텁으로 충분하다.
using System.Data;

namespace SqlKata
{
    public class Query
    {
        public Query Where(string column, object value) => this;

        public Query Join(string table, string first, string second) => this;

        public Query Select(params string[] columns) => this;
    }
}

namespace SqlKata.Execution
{
    public sealed class QueryFactory
    {
        public Query Query(string table) => new();
    }

    public static class QueryExtensions
    {
        public static Task<IReadOnlyList<T>> GetAsync<T>(this Query query, IDbTransaction? transaction = null)
            where T : new() => Task.FromResult<IReadOnlyList<T>>([]);

        public static Task<T?> FirstOrDefaultAsync<T>(this Query query, IDbTransaction? transaction = null)
            where T : class => Task.FromResult<T?>(null);

        public static Task<int> InsertAsync(this Query query, object values, IDbTransaction? transaction = null) =>
            Task.FromResult(1);

        public static Task<int> UpdateAsync(this Query query, object values, IDbTransaction? transaction = null) =>
            Task.FromResult(1);

        public static Task<int> DeleteAsync(this Query query, IDbTransaction? transaction = null) =>
            Task.FromResult(1);
    }
}

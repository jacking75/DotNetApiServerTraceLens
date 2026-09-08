namespace MinimalApiApp.Services;

public interface ICatalogService
{
    string Describe(string sku);
}

public sealed class CatalogService : ICatalogService
{
    public string Describe(string sku) => Format(sku);

    private static string Format(string sku) => $"sku:{sku}";
}

public interface IAuditService
{
    void Record(string message);
}

public sealed class AuditService : IAuditService
{
    public void Record(string message) => Console.WriteLine(message);
}

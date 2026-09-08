using MinimalApiApp.Services;

namespace MinimalApiApp.Endpoints;

/// <summary>등록이 확장 메서드 안에 숨어 있고, 그룹이 중첩된 경우를 재현한다.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/catalog");
        var nested = group.MapGroup("/items");

        // 메서드 그룹 핸들러
        nested.MapGet("/{sku}", GetItem);

        // 로컬 함수 핸들러
        nested.MapPost("/{sku}/audit", RecordAudit);

        // MapMethods로 여러 동사를 한 번에 등록한다.
        nested.MapMethods("/{sku}/touch", new[] { "PUT", "PATCH" }, (string sku) => Results.Ok(sku));

        return builder;

        static IResult RecordAudit(string sku, IAuditService audit)
        {
            audit.Record(sku);
            return Results.Accepted();
        }
    }

    private static IResult GetItem(string sku, ICatalogService catalog) => Results.Ok(catalog.Describe(sku));
}

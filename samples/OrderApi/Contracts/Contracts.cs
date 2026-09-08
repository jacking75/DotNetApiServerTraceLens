namespace OrderApi.Contracts;

public sealed record CreateOrderRequest(string CustomerId, string Sku, int Quantity);

public sealed record OrderView(string OrderId, string CustomerId, string Sku, int Quantity, string Status);

public sealed record ShipOrderRequest(string Carrier);

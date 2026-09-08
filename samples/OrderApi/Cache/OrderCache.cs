using Microsoft.Extensions.Caching.Memory;
using OrderApi.Contracts;

namespace OrderApi.Cache;

public interface IOrderCache
{
    OrderView? Get(string orderId);

    void Set(OrderView order);

    void Remove(string orderId);
}

public sealed class OrderCache(IMemoryCache cache) : IOrderCache
{
    private static string KeyOf(string orderId) => $"order:{orderId}";

    public OrderView? Get(string orderId) => cache.Get<OrderView>(KeyOf(orderId));

    public void Set(OrderView order) => cache.Set(KeyOf(order.OrderId), order, TimeSpan.FromMinutes(5));

    public void Remove(string orderId) => cache.Remove(KeyOf(orderId));
}

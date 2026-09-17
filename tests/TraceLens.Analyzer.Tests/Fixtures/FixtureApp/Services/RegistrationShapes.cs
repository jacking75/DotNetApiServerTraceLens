using Microsoft.Extensions.DependencyInjection;

namespace FixtureApp.Services;

/// <summary>자기 등록(<c>AddScoped&lt;Impl&gt;()</c>)으로만 등록한다.</summary>
public interface ISelfRegistered
{
    Task PingAsync();
}

public sealed class SelfRegistered : ISelfRegistered
{
    public Task PingAsync() => Task.CompletedTask;
}

/// <summary>팩토리 람다로 등록한다. 본문이 단일 <c>new</c>라 구현체를 확정할 수 있다.</summary>
public interface IFactoryRegistered
{
    Task PingAsync();
}

public sealed class FactoryRegistered : IFactoryRegistered
{
    public Task PingAsync() => Task.CompletedTask;
}

/// <summary>팩토리 본문이 분기라 확정할 수 없다. 구현체가 둘이라 "유일 구현체" 규칙도 쓸 수 없다.</summary>
public interface IAmbiguousFactory
{
    Task PingAsync();
}

public sealed class FirstAmbiguous : IAmbiguousFactory
{
    public Task PingAsync() => Task.CompletedTask;
}

public sealed class SecondAmbiguous : IAmbiguousFactory
{
    public Task PingAsync() => Task.CompletedTask;
}

/// <summary>open generic 등록(<c>typeof(IStore&lt;&gt;)</c>)으로 잇는다.</summary>
public interface IStore<T>
{
    Task<T?> LoadAsync(int id);
}

public sealed class MemoryStore<T> : IStore<T>
    where T : class, new()
{
    public Task<T?> LoadAsync(int id) => Task.FromResult<T?>(new T());
}

/// <summary>조건부 등록. 실행 시 어느 쪽이 쓰일지 정적으로 확정할 수 없다.</summary>
public interface IConditional
{
    Task PingAsync();
}

public sealed class ProductionConditional : IConditional
{
    public Task PingAsync() => Task.CompletedTask;
}

public sealed class DevelopmentConditional : IConditional
{
    public Task PingAsync() => Task.CompletedTask;
}

/// <summary>갈래마다 다른 구현체를 등록한다. 어느 쪽인지 확정할 수 없다.</summary>
public interface IEnvironmentSpecific
{
    Task PingAsync();
}

public sealed class DevelopmentEnvironment : IEnvironmentSpecific
{
    public Task PingAsync() => Task.CompletedTask;
}

public sealed class ProductionEnvironment : IEnvironmentSpecific
{
    public Task PingAsync() => Task.CompletedTask;
}

/// <summary>여러 등록 모양을 한곳에 모아 둔다. DiResolver가 각각을 어떻게 판정하는지 테스트가 확인한다.</summary>
public static class RegistrationShapes
{
    public static IServiceCollection AddShapes(this IServiceCollection services, bool development)
    {
        services.AddScoped<SelfRegistered>();
        services.AddScoped<ISelfRegistered>(provider => provider.GetRequiredService<SelfRegistered>());
        services.AddScoped<IFactoryRegistered>(_ => new FactoryRegistered());
        services.AddScoped<IAmbiguousFactory>(_ =>
        {
            if (development) return new DevelopmentConditionalAdapter();
            return new FirstAmbiguous();
        });
        services.AddScoped(typeof(IStore<>), typeof(MemoryStore<>));

        // 한 갈래에서만 등록한다. 구현체는 하나로 확정되지만 실행 시 등록 자체가 없을 수 있다.
        if (development)
        {
            services.AddScoped<IConditional, DevelopmentConditional>();
        }

        // 갈래마다 다른 구현체를 등록한다. 어느 쪽이 쓰일지 정적으로 확정할 수 없다.
        if (development)
        {
            services.AddScoped<IEnvironmentSpecific, DevelopmentEnvironment>();
        }
        else
        {
            services.AddScoped<IEnvironmentSpecific, ProductionEnvironment>();
        }

        return services;
    }
}

/// <summary>분기 본문에서만 쓰이는 어댑터. 팩토리 본문을 해석하지 못하는 경우를 만든다.</summary>
public sealed class DevelopmentConditionalAdapter : IAmbiguousFactory
{
    public Task PingAsync() => Task.CompletedTask;
}

/// <summary>
/// 등록된 인터페이스를 실제로 부르는 자리. 호출 지점이 있어야 DiResolver의 판정이 그래프에 나타난다.
/// </summary>
public sealed class RegistrationProbe(
    ISelfRegistered selfRegistered,
    IFactoryRegistered factoryRegistered,
    IAmbiguousFactory ambiguousFactory,
    IConditional conditional,
    IEnvironmentSpecific environmentSpecific,
    IStore<FixtureApp.Domain.Order> store)
{
    public async Task RunAsync()
    {
        await selfRegistered.PingAsync();
        await factoryRegistered.PingAsync();
        await ambiguousFactory.PingAsync();
        await conditional.PingAsync();
        await environmentSpecific.PingAsync();
        await store.LoadAsync(1);
    }
}

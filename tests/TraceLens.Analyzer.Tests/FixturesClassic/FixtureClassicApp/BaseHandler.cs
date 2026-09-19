namespace FixtureClassicApp.Services;

/// <summary>
/// <c>base.Method()</c> 회귀용 기반 클래스(T53).
/// 재정의 안에서 <c>base.HandleAsync()</c>를 부르는 것은 흔한 코드인데,
/// 재정의 추정 엣지를 함께 만들면 <c>OrderHandler.HandleAsync → OrderHandler.HandleAsync</c>
/// 자기 순환 엣지가 되어 재귀처럼 보인다.
/// </summary>
public abstract class BaseHandler
{
    public virtual Task HandleAsync(int id) => Task.CompletedTask;

    /// <summary>sealed가 아닌 보통의 virtual. 재정의가 하나뿐이면 재정의 추정 엣지가 붙는다.</summary>
    public virtual Task ValidateAsync(int id) => Task.CompletedTask;
}

public sealed class OrderHandler(IClock clock) : BaseHandler
{
    public override async Task HandleAsync(int id)
    {
        // base. 한정자가 붙은 호출이다. 실행되는 것은 언제나 BaseHandler.HandleAsync다.
        await base.HandleAsync(id);
        await Task.Delay(clock.Now.Millisecond % 2);
    }

    /// <summary>한정자 없는 가상 호출. 여기는 지금처럼 재정의 추정 엣지를 둔다.</summary>
    public async Task RunAsync(int id)
    {
        await ValidateAsync(id);
    }
}

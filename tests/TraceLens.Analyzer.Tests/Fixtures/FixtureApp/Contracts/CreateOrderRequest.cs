namespace FixtureApp.Contracts;

/// <summary>자동 프로퍼티만 있는 record. members에는 프로퍼티가 뜨고 nodes에는 안 뜨는지 검증한다.</summary>
public sealed record CreateOrderRequest(int CustomerId, string Note);

using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 이번 버전이 <b>인식은 하지만 해석하지 않는</b> 프레임워크 목록이다.
/// CLAUDE.md §5의 "검사하지 않은 것은 꺼짐으로 남긴다"를 입력 인식에도 적용한다 —
/// EF Core만 쓰는 프로젝트에서 DB 접근이 0건인데 규칙이 전부 "검사함"으로 보이는 일을 막는다.
///
/// 여기에 적는 것은 <b>해석하겠다는 약속이 아니라 해석하지 않았다는 사실</b>이다.
/// 엣지·접근·진입점을 만들지 않고 한계 문장만 만든다.
/// </summary>
internal static class UnreadFrameworks
{
    /// <param name="Prefix">네임스페이스 접두사. 이 접두사로 시작하는 외부 호출을 한 줄로 승격한다.</param>
    /// <param name="Reason">무엇을 읽지 않았는지. 사용자가 결과의 빈칸을 이 문장으로 설명할 수 있어야 한다.</param>
    private sealed record Entry(string Prefix, string Reason);

    private static readonly Entry[] Known =
    [
        new("Microsoft.EntityFrameworkCore",
            "DB 접근으로 해석하지 않았다(지금 버전은 SQL 리터럴·Dapper·SqlKata·설정한 queryApis만 읽는다)."),
        new("MongoDB.Driver", "DB 접근으로 해석하지 않았다(MongoDB 드라이버는 읽지 않는다)."),
        new("NHibernate", "DB 접근으로 해석하지 않았다(NHibernate 세션은 읽지 않는다)."),
        new("MediatR", "호출 엣지로 해석하지 않았다(Send/Publish에서 핸들러로 잇지 않는다)."),
        new("MassTransit", "메시지 소비자를 진입점으로 잡지 않았다."),
        new("Confluent.Kafka", "메시지 소비 루프를 진입점으로 잡지 않았다."),
        new("Hangfire", "예약 작업을 진입점으로 잡지 않았다."),
        new("Quartz", "예약 작업을 진입점으로 잡지 않았다."),
        new("Grpc", "gRPC 서비스를 진입점으로 잡지 않았다."),
        new("Microsoft.AspNetCore.SignalR", "SignalR Hub를 진입점으로 잡지 않았다."),
        new("System.Transactions", "TransactionScope 외의 System.Transactions 호출은 트랜잭션 경계로 읽지 않았다."),
    ];

    /// <summary>
    /// 외부 호출 집계에서 알려진 프레임워크를 골라 한계 문장으로 바꾼다.
    /// 표에 없는 네임스페이스는 문장으로 승격하지 않고 진단 패널의 목록에만 남는다.
    /// </summary>
    public static IReadOnlyList<string> Describe(IReadOnlyList<ExternalCallGroup> externalByNamespace)
    {
        var lines = new List<string>();
        foreach (var entry in Known)
        {
            var count = externalByNamespace
                .Where(group => group.Namespace.StartsWith(entry.Prefix, StringComparison.Ordinal))
                .Sum(group => group.Count);
            if (count > 0)
            {
                lines.Add($"`{entry.Prefix}` 호출 {count}건은 {entry.Reason}");
            }
        }

        return lines;
    }
}

namespace TraceLens.Analyzer.Models;

/// <summary>접근한 저장소의 종류.</summary>
public enum StorageKind
{
    Database,
    Cache,

    /// <summary>DB도 캐시도 아니지만 상태를 바꾸는 외부 호출(푸시·결제 등). 보상 처리 판정에 필요해 함께 담는다.</summary>
    External
}

public enum AccessOperation
{
    Read,
    Write,

    /// <summary>삭제. 캐시 무효화도 여기에 들어간다.</summary>
    Delete,
    Other
}

/// <summary>쓰기 한 단계가 실패했을 때 되돌려지는지에 대한 판정.</summary>
public enum CompensationKind
{
    /// <summary>트랜잭션 안이라 함께 롤백된다.</summary>
    Transaction,

    /// <summary>실패 경로에서 역연산을 찾았다.</summary>
    Yes,

    /// <summary>일부 실패 경로(보통 catch)에만 역연산이 있다.</summary>
    PartialPaths,

    /// <summary>역연산을 찾지 못했다.</summary>
    No,

    /// <summary>재실행해도 안전하거나 남아도 무해해서 되돌릴 필요가 없다.</summary>
    NotNeeded,

    /// <summary>취소 수단이 없다(푸시·메일·결제).</summary>
    Irreversible
}

/// <param name="Target">테이블 이름, 프로시저 이름, 또는 정규화한 캐시 키 패턴.</param>
/// <param name="Statement">화면에 그대로 보여줄 SQL 한 줄 또는 캐시 명령.</param>
public sealed record DataAccess(
    string Id,
    StorageKind Storage,
    string DataSource,
    string Target,
    AccessOperation Operation,
    string Statement,
    string MethodNodeId,
    string MethodDisplayName,
    string ClassId,
    string File,
    int Line,
    bool InTransaction,
    bool InLoop)
{
    /// <summary>
    /// 이 접근을 얼마나 확실하게 짚었는지. SQL 리터럴이나 상수 테이블 이름이면 확실이고,
    /// 빌더 체인의 테이블이 변수여서 <c>?</c>로 남겼거나 래퍼를 경유한 접근이면 추정이다.
    /// v7 이전 문서에는 없고, 없으면 확실로 읽는다.
    /// </summary>
    public FindingConfidence Confidence { get; init; } = FindingConfidence.Certain;

    /// <summary>추정일 때 그렇게 본 근거 한 줄. 확실이면 빈 문자열이다.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>트랜잭션 래퍼 안이면 그 래퍼 이름. 아니면 빈 문자열이다.</summary>
    public string TransactionWrapper { get; init; } = string.Empty;
}

/// <summary>트랜잭션 경계를 무엇으로 잡았는지.</summary>
public enum TransactionScopeKind
{
    /// <summary><c>BeginTransaction</c>/<c>Commit</c>을 이 메서드가 직접 부른다.</summary>
    Explicit,

    /// <summary>
    /// 람다를 받아 그 안에서 트랜잭션을 여닫는 래퍼 메서드에 넘겼다(<c>TransactionAsync(async (db, tx) =&gt; …)</c>).
    /// 경계는 래퍼 안에 있으므로 이 메서드에는 Begin/Commit이 보이지 않는다.
    /// </summary>
    Wrapper
}

/// <param name="DataSources">이 트랜잭션 경계 안에서 열린 커넥션의 데이터소스 이름.</param>
/// <param name="ExternalCalls">경계 안에서 부른 외부 호출(HTTP 등) 표시 이름.</param>
public sealed record TransactionScopeInfo(
    string Id,
    string MethodNodeId,
    string MethodDisplayName,
    string ClassId,
    string File,
    int StartLine,
    bool HasCommit,
    IReadOnlyList<string> DataSources,
    IReadOnlyList<string> AccessIds,
    IReadOnlyList<string> ExternalCalls,
    IReadOnlyList<string> CacheCalls)
{
    /// <summary>경계를 무엇으로 잡았는지. v7 이전 문서에는 없고, 없으면 <see cref="TransactionScopeKind.Explicit"/>로 읽는다.</summary>
    public TransactionScopeKind Kind { get; init; } = TransactionScopeKind.Explicit;

    /// <summary><see cref="TransactionScopeKind.Wrapper"/>일 때 그 래퍼 메서드의 표시 이름.</summary>
    public string WrapperName { get; init; } = string.Empty;
}

/// <param name="Detail">호출 대상 표시 이름과 데이터소스. 화면 둘째 줄에 쓴다.</param>
/// <param name="CompensationNote">그렇게 판정한 이유 한 줄.</param>
public sealed record WriteStep(
    int Order,
    string Label,
    string Detail,
    StorageKind Storage,
    string DataSource,
    string Target,
    AccessOperation Operation,
    CompensationKind Compensation,
    string CompensationNote,
    string File,
    int Line);

/// <summary>
/// 트랜잭션으로 묶이지 않은 쓰기 경로 하나. 화면의 "실패 지점 × 단계" 격자는 <see cref="Steps"/>로 만든다.
/// </summary>
public sealed record WritePath(
    string Id,
    string MethodNodeId,
    string MethodDisplayName,
    string ClassId,
    string File,
    int StartLine,
    bool InTransaction,
    string Note,
    IReadOnlyList<WriteStep> Steps,
    IReadOnlyList<string> FindingIds);

/// <param name="Kind">single · shard · cache. 읽기/쓰기 전용은 별도 플래그로 둔다.</param>
public sealed record DataSourceInfo(
    string Name,
    string Kind,
    int ShardCount,
    bool ReadOnly,
    bool WriteOnly,
    string Description,
    int AccessCount);

/// <summary>DB·캐시 보기 전체. <see cref="TraceLensConfig.IncludeDataAccess"/>가 꺼져 있으면 null이다.</summary>
/// <param name="ShardingEnabled">설정에 샤드 데이터소스가 있어 SHARD 규칙군을 켰는지.</param>
public sealed record DataAccessDocument(
    bool ShardingEnabled,
    IReadOnlyList<DataSourceInfo> DataSources,
    IReadOnlyList<DataAccess> Accesses,
    IReadOnlyList<TransactionScopeInfo> Transactions,
    IReadOnlyList<WritePath> WritePaths,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<RuleInfo> Rules);

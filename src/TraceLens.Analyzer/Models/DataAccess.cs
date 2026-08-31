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
    bool InLoop);

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
    IReadOnlyList<string> CacheCalls);

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

using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// DB·캐시 보기(보상 처리 포함)를 <c>FixtureDataApp</c>으로 검증한다.
/// 픽스처는 일부러 잘못 짠 코드를 담고 있어서, 각 규칙이 "그 자리"를 짚는지 확인할 수 있다.
/// MSBuild 로드가 느려서 분석은 한 번만 하고 모든 테스트가 그 결과를 함께 본다.
/// </summary>
[Collection(FixtureDataAppCollection.Name)]
public sealed class DataAccessAnalysisTests(FixtureDataAppFixture fixture)
{
    private async Task<DataAccessDocument> GetDataAccessAsync()
    {
        var result = await fixture.Result;
        return Assert.IsType<DataAccessDocument>(result.Graph.DataAccess);
    }

    /// <summary>
    /// 추정이 틀리는 것은 감수하지만 <b>확실로 틀리는</b> 판정은 도구의 신뢰를 직접 깎는다.
    /// 저장 프로시저를 메서드 이름만 보고 Read로 단정하던 자리를 막는다.
    /// </summary>
    [Fact]
    public async Task Scan_DoesNotAssertReadForStoredProcedures()
    {
        var data = await GetDataAccessAsync();

        var read = data.Accesses.Single(access => access.Target == "usp_UpdateOrder");
        Assert.Equal(AccessOperation.Unknown, read.Operation);
        Assert.Equal(FindingConfidence.Estimated, read.Confidence);
        Assert.Contains("본문을 읽지 않아", read.Note, StringComparison.Ordinal);

        // Execute 계열은 쓰기 후보로 남기되 확실도를 낮춘다.
        var write = data.Accesses.Single(access => access.Target == "usp_GrantReward");
        Assert.Equal(AccessOperation.Write, write.Operation);
        Assert.Equal(FindingConfidence.Estimated, write.Confidence);
    }

    /// <summary>
    /// <c>new TransactionScope(...)</c>는 호출식이 아니라 객체 생성이라 예전에는 경계로 잡히지 않았다.
    /// 그 결과 안에서 쓰는 코드가 "트랜잭션 없는 쓰기"로 COMP 규칙에 걸렸다.
    /// </summary>
    [Fact]
    public async Task Scan_TreatsTransactionScopeAsATransactionBoundary()
    {
        var data = await GetDataAccessAsync();

        var inScope = data.Accesses
            .Where(access => access.MethodDisplayName.Contains("WriteInsideScopeAsync", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, inScope.Length);
        Assert.All(inScope, access => Assert.True(access.InTransaction, "TransactionScope 안의 쓰기는 트랜잭션 안으로 잡혀야 한다."));
        Assert.DoesNotContain(
            data.WritePaths,
            path => path.MethodDisplayName.Contains("WriteInsideScopeAsync", StringComparison.Ordinal) && !path.InTransaction);
    }

    [Fact]
    public async Task Scan_ReadsSqlTablesCacheKeysAndDataSources()
    {
        var data = await GetDataAccessAsync();

        Assert.True(data.ShardingEnabled, "설정에 shard 데이터소스가 있으므로 샤딩 규칙군이 켜져야 한다.");
        Assert.Contains(data.DataSources, source => source.Name == "GameDb" && source.Kind == "shard" && source.ShardCount == 8);
        Assert.Contains(data.DataSources, source => source.Name == "Redis" && source.Kind == "cache");
        Assert.Contains(data.DataSources, source => source.Name == "LogDb" && source.WriteOnly);

        // SQL 리터럴에서 테이블과 동작을 읽어야 한다.
        Assert.Contains(data.Accesses, access =>
            access.Target == "user_item" && access.Operation == AccessOperation.Write && access.DataSource == "GameDb");
        Assert.Contains(data.Accesses, access =>
            access.Target == "user_profile" && access.Operation == AccessOperation.Read);
        Assert.Contains(data.Accesses, access =>
            access.Target == "item_log" && access.DataSource == "LogDb");

        // SQL 동사로 시작할 뿐인 평범한 문자열은 쿼리로 보지 않는다.
        Assert.DoesNotContain(data.Accesses, access => access.MethodDisplayName.StartsWith("TextUtil.", StringComparison.Ordinal));

        // 캐시는 보간 문자열을 키 패턴으로 정규화한다.
        Assert.Contains(data.Accesses, access =>
            access.Storage == StorageKind.Cache && access.Target == "lock:craft:{userNo}");
        Assert.Contains(data.Accesses, access =>
            access.Storage == StorageKind.Cache && access.Target == "user:{friendNo}:profile");
    }

    [Fact]
    public async Task WritePath_ClassifiesCompensationPerStep()
    {
        var data = await GetDataAccessAsync();
        var craft = Assert.Single(data.WritePaths, path => path.MethodDisplayName == "CraftService.CraftAsync");

        Assert.False(craft.InTransaction);
        Assert.Equal(4, craft.Steps.Count);

        // 1번 재료 차감은 catch의 RestoreMaterialsAsync로 되돌아간다(Consume↔Restore 이름 규칙).
        Assert.Equal(CompensationKind.Yes, craft.Steps[0].Compensation);
        // 2번 완성품 지급은 역연산(RemoveItemAsync)을 부르지 않아 그대로 남는다.
        Assert.Equal(CompensationKind.No, craft.Steps[1].Compensation);
        Assert.Equal("user_item", craft.Steps[1].Target);
        // 3번 캐시 무효화는 재실행해도 안전하고, 4번 로그는 남아도 무해하다.
        Assert.Equal(CompensationKind.NotNeeded, craft.Steps[2].Compensation);
        Assert.Equal(CompensationKind.NotNeeded, craft.Steps[3].Compensation);
    }

    [Fact]
    public async Task WritePath_MarksTransactionPathAsSafe()
    {
        var data = await GetDataAccessAsync();
        var buy = Assert.Single(data.WritePaths, path => path.MethodDisplayName == "ShopService.BuyAsync");

        Assert.True(buy.InTransaction, "쓰기가 모두 트랜잭션 안이면 보상 격자가 필요 없다.");
        Assert.All(buy.Steps, step =>
            Assert.True(step.Compensation is CompensationKind.Transaction or CompensationKind.NotNeeded));
    }

    [Fact]
    public async Task Rules_FindMissingCompensationAndEarlyReturnPath()
    {
        var data = await GetDataAccessAsync();

        var missing = Assert.Single(data.Findings, finding =>
            finding.RuleId == "COMP-002" && finding.Symbol == "CraftService.CraftAsync" &&
            finding.What.Contains("AddItemAsync", StringComparison.Ordinal));
        Assert.Equal(FindingSeverity.Critical, missing.Severity);
        Assert.Contains("user_item", missing.Why);

        // catch에는 되돌리기가 있지만 return false 경로에는 없다.
        var early = Assert.Single(data.Findings, finding => finding.RuleId == "COMP-006");
        Assert.Equal("GuildService.JoinAsync", early.Symbol);

        // 되돌릴 수 없는 푸시 발송 뒤에 실패 가능한 쓰기가 온다.
        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "COMP-005" && finding.Symbol == "MailSendService.SendAsync");

        // 캐시를 먼저 갱신하고 DB 실패를 되돌리지 않는다.
        var cacheFirst = Assert.Single(data.Findings, finding => finding.RuleId == "CACHE-002");
        Assert.Equal("AttendanceService.CheckAsync", cacheFirst.Symbol);
    }

    [Fact]
    public async Task Rules_FindTransactionCacheAndConnectionProblems()
    {
        var data = await GetDataAccessAsync();

        var spanning = Assert.Single(data.Findings, finding => finding.RuleId == "TX-003");
        Assert.Equal("MailReceiveService.ReceiveAllAsync", spanning.Symbol);
        Assert.Contains("LogDb", spanning.What);

        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "TX-001" && finding.Symbol == "MailReceiveService.ReceiveAllAsync");
        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "CACHE-007" && finding.Symbol == "MailReceiveService.ReceiveAllAsync");
        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "CACHE-003" && finding.Symbol == "CraftService.CraftAsync");
        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "CACHE-005" && finding.Symbol == "FriendService.GetFriendProfilesAsync");
        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "PERF-001" && finding.Symbol == "FriendService.GetFriendProfilesAsync");
        Assert.Contains(data.Findings, finding => finding.RuleId == "PERF-002");
        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "CONN-001" && finding.Symbol == "LegacyStatRepository.LoadAllAsync");
        Assert.Contains(data.Findings, finding => finding.RuleId == "SEC-001");
        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "RW-001" && finding.Symbol == "RankingRepository.UpdateSnapshotAsync");
        Assert.Contains(data.Findings, finding => finding.RuleId == "CONN-002");
    }

    [Fact]
    public async Task Rules_FindShardProblemsOnlyWhereExpected()
    {
        var data = await GetDataAccessAsync();

        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "SHARD-001" && finding.Symbol == "RankingService.BuildTopAsync");
        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "SHARD-004" && finding.Symbol == "DevToolController.Dump");
        Assert.Contains(data.Findings, finding =>
            finding.RuleId == "SHARD-002" && finding.Symbol == "FriendService.GetFriendProfilesAsync");

        // 본문 값을 그대로 쓰는 액션만 걸리고, 인증 컨텍스트를 읽는 액션은 걸리지 않아야 한다.
        var unverified = data.Findings.Where(finding => finding.RuleId == "SHARD-005").Select(finding => finding.Symbol).ToArray();
        Assert.Contains("ItemController.Craft", unverified);
        Assert.DoesNotContain("GuildController.Join", unverified);
    }

    /// <summary>
    /// 트랜잭션 경계를 선형 카운터로 세면 `if` 안의 Commit이 그 뒤 코드까지 밖으로 밀어낸다.
    /// 경계는 구문 범위다 — 커밋 여부와 무관하게 `using` 블록 끝까지다(T56).
    /// </summary>
    [Fact]
    public async Task Transaction_TreatsScopeAsSyntacticRange_NotLinearCounter()
    {
        var data = await GetDataAccessAsync();

        Assert.DoesNotContain(data.Findings, finding =>
            finding.Symbol == "TransactionEdgeCases.ConditionalCommitAsync" &&
            finding.RuleId.StartsWith("COMP-", StringComparison.Ordinal));
    }

    /// <summary>
    /// 항상 트랜잭션 래퍼 안에서만 불리는 리포지터리를 COMP로 잡으면 오탐이다.
    /// 판정 근거에 호출자 문맥이 남아야 "왜 안 걸렸는지"를 되짚을 수 있다(T56).
    /// </summary>
    [Fact]
    public async Task Compensation_SkipsMethodsAlwaysCalledInsideTransaction()
    {
        var data = await GetDataAccessAsync();

        Assert.DoesNotContain(data.Findings, finding =>
            finding.Symbol == "InventoryRepository.MoveAsync" &&
            finding.RuleId.StartsWith("COMP-", StringComparison.Ordinal));

        var path = Assert.Single(data.WritePaths, item => item.MethodDisplayName == "InventoryRepository.MoveAsync");
        Assert.Equal(TransactionContext.Always, path.CallerTransaction);
        Assert.True(path.InTransaction);
        Assert.Contains("호출자", path.Note);
    }

    /// <summary>`if/else`의 배타 분기에 있는 쓰기 2개는 순차 2단계가 아니다(T56).</summary>
    [Fact]
    public async Task Compensation_CountsExclusiveBranchesAsOneStep()
    {
        var data = await GetDataAccessAsync();

        Assert.DoesNotContain(data.WritePaths, item =>
            item.MethodDisplayName == "TransactionEdgeCases.ExclusiveBranchesAsync");
    }

    [Fact]
    public async Task RuleCatalog_ShowsWhichRulesAreChecked()
    {
        var data = await GetDataAccessAsync();

        Assert.Contains(data.Rules, rule => rule.Id == "COMP-002" && rule.Enabled);
        Assert.Contains(data.Rules, rule => rule.Id == "IDX-001" && !rule.Enabled);
        Assert.Contains(data.Rules, rule => rule.Id == "SHARD-002" && rule.ShardOnly && rule.Enabled);
        Assert.All(data.Findings, finding =>
            Assert.Contains(data.Rules, rule => rule.Id == finding.RuleId && rule.Enabled));
    }

}

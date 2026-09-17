using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

/// <summary>
/// T1(람다 본문 호출·트랜잭션 래퍼) · T2(쿼리 빌더) · T3(캐시 래퍼)를 <c>FixtureDataApp</c>으로 검증한다.
/// </summary>
[Collection(FixtureDataAppCollection.Name)]
public sealed class LambdaAndBuilderTests(FixtureDataAppFixture fixture)
{
    private const string CreateAsync =
        "FixtureDataApp.Services.BuilderOrderService.CreateAsync(int,string)";

    [Fact]
    public async Task LambdaBody_CallsBecomeEstimatedEdges()
    {
        var graph = (await fixture.Result).Graph;

        var insert = Assert.Single(graph.Edges, edge =>
            edge.From == CreateAsync && edge.To.Contains("BuilderOrderRepository.InsertOrderAsync", StringComparison.Ordinal));
        Assert.Equal(CallConfidence.Estimated, insert.Confidence);

        Assert.Contains(graph.Edges, edge =>
            edge.From == CreateAsync && edge.To.Contains("LedgerRepository.AppendAsync", StringComparison.Ordinal));

        var site = Assert.Single(graph.CallSites, item =>
            item.From == CreateAsync && item.To.Contains("InsertOrderAsync", StringComparison.Ordinal));
        Assert.True(site.ViaLambda, "람다 본문 안의 호출이라고 표시해야 한다.");
        Assert.Equal(CallConfidence.Estimated, site.Confidence);
        Assert.Contains("람다 본문", site.Note);
        Assert.Contains("TransactionAsync", site.Note);
    }

    [Fact]
    public async Task LambdaBody_CoversTaskRunAndLinq()
    {
        var graph = (await fixture.Result).Graph;

        Assert.Contains(graph.Edges, edge =>
            edge.From.Contains("BuilderOrderService.NotifyLaterAsync", StringComparison.Ordinal) &&
            edge.To.Contains("LedgerRepository.AppendFireAndForgetAsync", StringComparison.Ordinal));
        Assert.Contains(graph.Edges, edge =>
            edge.From.Contains("BuilderOrderService.Describe", StringComparison.Ordinal) &&
            edge.To.Contains("BuilderOrderService.Format", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MinimalApiHandlerLambda_IsNotAttributedTwice()
    {
        // 진입점 핸들러로 등록된 람다는 자기 노드를 가진다. 감싼 메서드가 그 안의 호출을 다시 가져가면 안 된다.
        var graph = (await fixture.Result).Graph;
        var duplicated = graph.CallSites
            .Where(site => site.ViaLambda)
            .GroupBy(site => (site.File, site.Line, site.To))
            .Where(group => group.Select(item => item.From).Distinct(StringComparer.Ordinal).Count() > 1)
            .ToArray();
        Assert.Empty(duplicated);
    }

    [Fact]
    public async Task TransactionWrapper_MarksWritesAsRolledBack()
    {
        var data = Assert.IsType<DataAccessDocument>((await fixture.Result).Graph.DataAccess);

        var path = Assert.Single(data.WritePaths, item => item.MethodDisplayName == "BuilderOrderService.CreateAsync");
        Assert.True(path.InTransaction, "트랜잭션 래퍼에 넘긴 람다 안의 쓰기는 함께 롤백된다.");
        Assert.All(path.Steps, step => Assert.Equal(CompensationKind.Transaction, step.Compensation));
        Assert.Contains(path.Steps, step => step.CompensationNote.Contains("트랜잭션 래퍼", StringComparison.Ordinal));

        Assert.Contains(data.Transactions, item =>
            item.Kind == TransactionScopeKind.Wrapper &&
            item.MethodDisplayName == "BuilderOrderService.CreateAsync" &&
            item.WrapperName.Contains("TransactionAsync", StringComparison.Ordinal));

        Assert.DoesNotContain(data.Findings, finding =>
            finding.RuleId == "COMP-002" && finding.Symbol == "BuilderOrderService.CreateAsync");
    }

    [Fact]
    public async Task QueryBuilder_ReadsTableAndTransactionArgument()
    {
        var data = Assert.IsType<DataAccessDocument>((await fixture.Result).Graph.DataAccess);

        var insert = Assert.Single(data.Accesses, access =>
            access.Target == "player_order" && access.Operation == AccessOperation.Write);
        Assert.Equal(StorageKind.Database, insert.Storage);
        Assert.Equal(FindingConfidence.Certain, insert.Confidence);
        Assert.True(insert.InTransaction, "terminal 호출에 transaction 인자를 넘겼다.");
        Assert.Equal("INSERT player_order", insert.Statement);

        Assert.Contains(data.Accesses, access =>
            access.Target == "player_ledger" && access.Operation == AccessOperation.Write);

        // 지역 변수에 담은 체인도 root까지 거슬러 올라가야 한다.
        Assert.Contains(data.Accesses, access =>
            access.Target == "player_order" && access.Operation == AccessOperation.Read);

        // 테이블이 변수면 확정하지 않는다.
        var unknown = Assert.Single(data.Accesses, access =>
            access.MethodDisplayName == "BuilderOrderRepository.UpdateByTableAsync");
        Assert.Equal("?", unknown.Target);
        Assert.Equal(FindingConfidence.Estimated, unknown.Confidence);
        Assert.Contains("table", unknown.Note);
    }

    [Fact]
    public async Task CacheWrapper_ReportsExpiryOnlyInsideTheWrapper()
    {
        var data = Assert.IsType<DataAccessDocument>((await fixture.Result).Graph.DataAccess);

        // 래퍼를 부르는 쪽은 캐시 접근이 아니다 — 만료를 이 자리에서 판단할 수 없다.
        Assert.DoesNotContain(data.Accesses, access =>
            access.MethodDisplayName.StartsWith("SessionService.", StringComparison.Ordinal));
        Assert.DoesNotContain(data.Findings, finding =>
            finding.RuleId == "CACHE-003" && finding.Symbol.StartsWith("SessionService.", StringComparison.Ordinal));

        // 만료를 넘기는 래퍼 본문에는 나지 않고, 넘기지 않는 래퍼 본문에만 한 건 난다.
        Assert.DoesNotContain(data.Findings, finding =>
            finding.RuleId == "CACHE-003" && finding.Symbol == "SessionCache.SetAsync");
        Assert.Single(data.Findings, finding =>
            finding.RuleId == "CACHE-003" && finding.Symbol == "SessionCache.SetForeverAsync");
    }

    [Fact]
    public async Task Accesses_AreNotListedTwiceForTheSameLine()
    {
        var data = Assert.IsType<DataAccessDocument>((await fixture.Result).Graph.DataAccess);
        var duplicated = data.Accesses
            .GroupBy(access => (access.File, access.Line, access.Target, access.Operation))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.Empty(duplicated);
    }
}

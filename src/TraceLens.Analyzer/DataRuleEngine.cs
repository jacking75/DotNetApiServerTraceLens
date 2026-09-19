using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 스캔 결과와 쓰기 경로에 규칙을 적용해 점검 항목을 만든다.
/// 규칙 카탈로그는 아직 구현하지 않은 것까지 함께 담아, 화면에서 "무엇을 검사하지 않는지"도 보이게 한다.
/// </summary>
public sealed class DataRuleEngine(ILogger<DataRuleEngine> logger)
{
    public DataAccessDocument Build(
        DataAccessScanResult scan,
        IReadOnlyList<WritePathAnalysis> writePaths,
        IReadOnlyDictionary<string, MethodEffect> effects,
        StartupFlowDocument? startupFlow,
        TraceLensConfig config,
        CancellationToken cancellationToken = default)
    {
        var options = config.Db;
        var sharding = options.ShardingEnabled;
        var findings = new List<Finding>();
        var pathFindings = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Add(Finding finding, string? pathId = null)
        {
            findings.Add(finding);
            if (pathId is null)
            {
                return;
            }

            if (!pathFindings.TryGetValue(pathId, out var list))
            {
                pathFindings[pathId] = list = [];
            }

            list.Add(finding.Id);
        }

        var readOnlySources = options.DataSources
            .Where(source => source.ReadOnly)
            .Select(source => source.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var facts in scan.Methods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connectionKey = facts.Sites.FirstOrDefault(site => site.Kind == SiteKind.ConnectionOpen)?.ShardKeyIdentifier;
            foreach (var site in facts.Sites)
            {
                switch (site.Kind)
                {
                    case SiteKind.ConnectionOpen:
                        if (!site.InUsing)
                        {
                            Add(Create("CONN-001", FindingConfidence.Estimated, facts, site,
                                "using 없이 커넥션을 연다",
                                "예외가 나면 커넥션이 풀로 돌아가지 않는다. 같은 메서드 안에 Dispose 호출이 보이지 않는다.",
                                "await using으로 감싼다.",
                                [Evidence("bad", "감싸지 않은 커넥션", site.Statement.Length > 0 ? site.Statement : site.Name, $"{facts.File}:{site.Line}")]));
                        }

                        if (sharding && site.InLoop)
                        {
                            Add(Create("SHARD-001", FindingConfidence.Certain, facts, site,
                                "반복문 안에서 커넥션을 열어 전 샤드를 순회한다",
                                "요청 하나가 샤드 수만큼 커넥션을 연다. 집계라면 의도일 수 있으니 확인하고, 아니라면 샤드 키를 정해야 한다.",
                                "의도된 집계면 “의도된 것으로 표시”로 남기고, 아니라면 샤드 키를 정해 한 번만 연다.",
                                [Evidence("bad", "반복문 안의 커넥션 열기", site.Name, $"{facts.File}:{site.Line}")]));
                        }

                        if (sharding && site.ShardKeyIsLiteral)
                        {
                            Add(Create("SHARD-004", FindingConfidence.Certain, facts, site,
                                "샤드 번호가 상수로 들어간다",
                                "운영 도구가 아니면 대부분 실수다. 특정 샤드에만 접근하게 된다.",
                                "userNo에서 샤드를 계산하도록 바꾼다.",
                                [Evidence("bad", "상수 인자", $"{site.Name}({site.ShardKeyIdentifier})", $"{facts.File}:{site.Line}")]));
                        }

                        break;

                    case SiteKind.Db:
                        // SEC-001은 SQL 본문을 읽는 규칙이다. 빌더 체인에서 합성한 문장은 원문이 아니므로 검사하지 않는다.
                        if (site.DynamicSql && !site.IsSynthesized)
                        {
                            Add(Create("SEC-001", FindingConfidence.Certain, facts, site,
                                "문자열 보간·결합으로 SQL을 만든다",
                                "상수만 이어 붙인 것이 아니라 외부에서 온 값이 그대로 들어갈 수 있다.",
                                "파라미터로 넘긴다.",
                                [Evidence("bad", "만들어지는 SQL", site.Statement, $"{facts.File}:{site.Line}")]));
                        }

                        if (site.InLoop)
                        {
                            Add(Create("PERF-001", FindingConfidence.Certain, facts, site,
                                "반복문 안에서 쿼리를 실행한다",
                                "반복 횟수만큼 왕복이 생긴다. IN 조회나 배치로 묶을 자리다.",
                                "키를 모아 한 번에 조회한다.",
                                [Evidence("bad", "반복문 안의 쿼리", site.Statement, $"{facts.File}:{site.Line}")]));
                        }

                        if (site.IsWrite && readOnlySources.Contains(ResolveDataSource(facts, site)))
                        {
                            Add(Create("RW-001", FindingConfidence.Certain, facts, site,
                                "읽기 전용으로 표시한 데이터소스에 쓰기를 실행한다",
                                "설정에서 readOnly로 잡은 데이터소스다. 복제본 쓰기는 실패하거나 조용히 덮인다.",
                                "마스터 커넥션으로 연다.",
                                [Evidence("bad", "쓰기 쿼리", site.Statement, $"{ResolveDataSource(facts, site)} · {facts.File}:{site.Line}")]));
                        }

                        if (sharding && connectionKey is not null)
                        {
                            var mismatch = site.ArgumentIdentifiers.FirstOrDefault(identifier =>
                                LooksLikeShardKey(identifier, options) &&
                                !string.Equals(identifier, connectionKey, StringComparison.OrdinalIgnoreCase));
                            if (mismatch is not null)
                            {
                                Add(Create("SHARD-002", FindingConfidence.Estimated, facts, site,
                                    $"커넥션은 {connectionKey}로 열고 쿼리는 {mismatch}로 실행한다",
                                    "두 값의 샤드가 다르면 그 데이터는 이 커넥션에 없다. 빈 결과이거나 남의 행에 맞는다.",
                                    "대상 유저별로 샤드를 다시 골라 묶어서 조회한다.",
                                    [
                                        Evidence("ok", "커넥션을 연 키", connectionKey ?? string.Empty, facts.File),
                                        Evidence("bad", "쿼리 파라미터", mismatch, $"{facts.File}:{site.Line}")
                                    ]));
                            }
                        }

                        break;

                    case SiteKind.Cache:
                        // SiteKind.Cache는 만료 인자를 이 자리에서 판단할 수 있는 클라이언트 호출뿐이다.
                        // 솔루션 안 래퍼를 경유한 접근은 여기까지 오지 않는다(IsCacheApi 참고).
                        if (site.Operation == AccessOperation.Write && !site.HasExpiryArgument &&
                            site.Statement.StartsWith("SET", StringComparison.OrdinalIgnoreCase))
                        {
                            Add(Create("CACHE-003", FindingConfidence.Estimated, facts, site,
                                "TTL 없이 캐시에 쓴다",
                                "만료가 없는 키가 쌓인다. 락이면 영구 잠김으로 이어진다.",
                                "만료 시간을 함께 넘긴다.",
                                [Evidence("bad", "만료 인자가 없다", site.Statement, $"{facts.File}:{site.Line}")]));
                        }

                        if (site.Statement.StartsWith("KEYS", StringComparison.OrdinalIgnoreCase) ||
                            site.Statement.StartsWith("FLUSH", StringComparison.OrdinalIgnoreCase))
                        {
                            Add(Create("CACHE-004", FindingConfidence.Certain, facts, site,
                                "전체 순회 명령을 쓴다",
                                "Redis를 블로킹한다. 키가 많으면 그동안 모든 요청이 멈춘다.",
                                "SCAN으로 바꾸거나 조회용 인덱스를 따로 둔다.",
                                [Evidence("bad", "명령", site.Statement, $"{facts.File}:{site.Line}")]));
                        }

                        if (site.Operation == AccessOperation.Read && site.InLoop)
                        {
                            Add(Create("CACHE-005", FindingConfidence.Certain, facts, site,
                                "반복문 안에서 캐시를 하나씩 읽는다",
                                "왕복이 반복 횟수만큼 늘어난다.",
                                "MGET·파이프라인으로 묶는다.",
                                [Evidence("bad", "반복문 안의 조회", site.Statement, $"{facts.File}:{site.Line}")]));
                        }

                        if (site.InTransaction)
                        {
                            Add(Create("CACHE-007", FindingConfidence.Certain, facts, site,
                                "DB 트랜잭션 안에서 캐시를 호출한다",
                                "커밋 전에 캐시 왕복을 기다린다. 커밋이 실패하면 캐시만 어긋난다.",
                                "커밋 이후로 옮긴다.",
                                [Evidence("bad", "트랜잭션 안", site.Statement, $"{facts.File}:{site.Line}")]));
                        }

                        break;

                    case SiteKind.Plain:
                        // 서비스는 쿼리를 직접 쓰지 않는다. 호출한 메서드가 저장소를 건드리면 그 자리도 같은 규칙으로 본다.
                        if (site.CalleeNodeId is null ||
                            !effects.TryGetValue(site.CalleeNodeId, out var calleeEffect) ||
                            calleeEffect.Writes.Concat(calleeEffect.Reads)
                                .All(item => item.Storage != StorageKind.Database))
                        {
                            break;
                        }

                        if (site.InLoop)
                        {
                            Add(Create("PERF-001", FindingConfidence.Certain, facts, site,
                                $"반복문 안에서 {site.Name}을(를) 부른다 — 그 안에서 쿼리가 실행된다",
                                "반복 횟수만큼 왕복이 생긴다. 키를 모아 한 번에 조회하는 편이 낫다.",
                                "IN 조회나 배치로 묶는다.",
                                [Evidence("bad", "반복문 안의 호출", $"{site.ReceiverText}.{site.Name}", $"{facts.File}:{site.Line}")]));
                        }

                        if (sharding && connectionKey is not null)
                        {
                            var other = site.ArgumentIdentifiers.FirstOrDefault(identifier =>
                                LooksLikeShardKey(identifier, options) &&
                                !string.Equals(identifier, connectionKey, StringComparison.OrdinalIgnoreCase));
                            if (other is not null)
                            {
                                Add(Create("SHARD-002", FindingConfidence.Estimated, facts, site,
                                    $"커넥션은 {connectionKey}로 열고 {site.Name}에는 {other}를 넘긴다",
                                    "두 값의 샤드가 다르면 그 데이터는 이 커넥션에 없다. 빈 결과이거나 남의 행에 맞는다.",
                                    "대상 유저별로 샤드를 다시 골라 묶어서 조회한다.",
                                    [
                                        Evidence("ok", "커넥션을 연 키", connectionKey, facts.File),
                                        Evidence("bad", "호출에 넘긴 키", other, $"{facts.File}:{site.Line}")
                                    ]));
                            }
                        }

                        break;

                    case SiteKind.SyncWait:
                        if (facts.Sites.Any(other => other.Kind is SiteKind.Db or SiteKind.Cache))
                        {
                            Add(Create("PERF-002", FindingConfidence.Certain, facts, site,
                                "비동기 저장소 호출을 동기로 기다린다",
                                "스레드풀을 막는다. 부하가 몰리면 커넥션 고갈로 이어진다.",
                                "await로 바꾸고 호출부까지 async로 잇는다.",
                                [Evidence("bad", "동기 대기", site.Statement, $"{facts.File}:{site.Line}")]));
                        }

                        break;
                }
            }

            var methodEffect = effects.TryGetValue(facts.NodeId, out var found) ? found : MethodEffect.Empty;
            if (sharding && facts.IsControllerAction && facts.RequestBoundParameters.Count > 0 &&
                !facts.ReferencesUserContext && methodEffect.HasWrite && facts.Sites.Count > 0)
            {
                Add(Create("SHARD-005", FindingConfidence.Estimated, facts, facts.Sites[0],
                    "요청에서 받은 값을 검증 없이 샤드 키로 넘긴다",
                    "인증 토큰의 사용자와 대조하는 코드가 이 액션에 없다. 남의 번호를 넣으면 그 유저의 샤드가 열린다.",
                    "토큰의 사용자 번호와 같은지 확인한 뒤 다르면 요청을 끊는다.",
                    [
                        Evidence("bad", "요청에서 바인딩된 파라미터", string.Join(", ", facts.RequestBoundParameters), facts.File),
                        Evidence("bad", "인증 컨텍스트 참조", "User·HttpContext를 한 번도 읽지 않는다", $"{facts.File}:{facts.StartLine}")
                    ]));
            }
        }

        foreach (var transaction in scan.Transactions)
        {
            if (transaction.DataSources.Count > 1)
            {
                Add(new Finding(
                    NextId(findings.Count), "TX-003", FindingSeverity.Critical, FindingConfidence.Certain,
                    $"한 트랜잭션이 커넥션 {transaction.DataSources.Count}개({string.Join(" · ", transaction.DataSources)})에 걸쳐 있다",
                    "분산 트랜잭션이 없으면 한쪽만 반영될 수 있다. 커밋이 실패해도 다른 쪽 쓰기는 남는다.",
                    "다른 저장소 쓰기는 커밋 이후로 옮기거나 실패해도 무시되는 큐로 넘긴다.",
                    transaction.MethodDisplayName, transaction.File, transaction.StartLine,
                    transaction.MethodNodeId, null, null, null,
                    [Evidence("bad", "경계 안에서 열린 커넥션", string.Join(" · ", transaction.DataSources), $"{transaction.File}:{transaction.StartLine}")]));
            }

            if (transaction.ExternalCalls.Count > 0)
            {
                Add(new Finding(
                    NextId(findings.Count), "TX-001", FindingSeverity.Warning, FindingConfidence.Certain,
                    "트랜잭션 안에서 외부 호출을 기다린다",
                    "락을 잡은 채로 외부 응답을 기다린다. 상대가 느려지면 커넥션이 고갈된다.",
                    "외부 호출을 커밋 앞뒤로 옮긴다.",
                    transaction.MethodDisplayName, transaction.File, transaction.StartLine,
                    transaction.MethodNodeId, null, null, null,
                    [Evidence("bad", "경계 안 외부 호출", string.Join(" · ", transaction.ExternalCalls), transaction.File)]));
            }
        }

        foreach (var analysis in writePaths)
        {
            var path = analysis.Path;
            if (path.InTransaction)
            {
                continue;
            }

            Add(new Finding(
                NextId(findings.Count), "COMP-001", FindingSeverity.Info, FindingConfidence.Certain,
                $"트랜잭션 없이 저장소를 {path.Steps.Count}번 바꾼다",
                "중간에 실패하면 앞 단계를 사람이 되돌려야 한다. 같은 커넥션의 쓰기라면 트랜잭션으로 묶는 편이 간단하다.",
                "묶을 수 있는 쓰기는 한 트랜잭션으로 모은다.",
                path.MethodDisplayName, path.File, path.StartLine, path.MethodNodeId, path.Id, null, null,
                [Evidence("bad", "쓰기 단계", string.Join(" → ", path.Steps.Select(step => step.Label)), $"{path.File}:{path.StartLine}")]),
                path.Id);

            for (var index = 0; index < path.Steps.Count - 1; index++)
            {
                var step = path.Steps[index];
                if (step.Compensation == CompensationKind.No)
                {
                    var rule = step.Storage == StorageKind.Cache && path.Steps.Skip(index + 1)
                        .Any(later => later.Storage == StorageKind.Database)
                        ? "CACHE-002"
                        : "COMP-002";
                    Add(new Finding(
                        NextId(findings.Count), rule, FindingSeverity.Critical, FindingConfidence.Certain,
                        $"{step.Order}번 단계({step.Label})가 실패 시 되돌아가지 않는다",
                        $"뒤 단계에서 실패하면 {(step.Target.Length > 0 ? step.Target : step.Label)}가 그대로 남는다. {step.CompensationNote}",
                        "역연산을 실패 경로에 추가하거나, 앞뒤 쓰기를 한 트랜잭션으로 묶는다.",
                        path.MethodDisplayName, path.File, step.Line, path.MethodNodeId, path.Id, null, step.Target,
                        [
                            Evidence("bad", $"{step.Order}번 단계", step.Detail, $"{path.File}:{step.Line}"),
                            Evidence("bad", "되돌리기", step.CompensationNote, string.Empty),
                            Evidence("ok", "뒤에 오는 단계", string.Join(" · ", path.Steps.Skip(index + 1).Select(later => later.Label)), "이 중 하나라도 실패하면 남는다")
                        ]),
                        path.Id);
                }
                else if (step.Compensation == CompensationKind.PartialPaths)
                {
                    Add(new Finding(
                        NextId(findings.Count), "COMP-006", FindingSeverity.Critical, FindingConfidence.Certain,
                        $"{step.Order}번 단계({step.Label})가 일부 실패 경로에서만 되돌아간다",
                        $"예외 경로에는 되돌리기가 있어 괜찮아 보이지만 다른 경로에는 없다. {step.CompensationNote}",
                        "되돌리기를 공통 함수로 빼서 두 경로 모두에서 부르거나 트랜잭션으로 묶는다.",
                        path.MethodDisplayName, path.File, step.Line, path.MethodNodeId, path.Id, null, step.Target,
                        [
                            Evidence("bad", $"{step.Order}번 단계", step.Detail, $"{path.File}:{step.Line}"),
                            Evidence("bad", "빠진 경로", step.CompensationNote, string.Empty)
                        ]),
                        path.Id);
                }
                else if (step.Compensation == CompensationKind.Irreversible)
                {
                    Add(new Finding(
                        NextId(findings.Count), "COMP-005", FindingSeverity.Warning, FindingConfidence.Certain,
                        $"되돌릴 수 없는 작업({step.Label}) 뒤에 실패할 수 있는 쓰기가 온다",
                        "뒤 단계가 실패하면 이미 나간 것은 취소할 수 없다. 순서만 바꿔도 사라지는 문제다.",
                        "되돌릴 수 있는 쓰기를 모두 끝낸 뒤 마지막에 실행한다.",
                        path.MethodDisplayName, path.File, step.Line, path.MethodNodeId, path.Id, null, step.Target,
                        [
                            Evidence("bad", "되돌릴 수 없는 단계", step.Detail, $"{path.File}:{step.Line}"),
                            Evidence("bad", "그 뒤의 쓰기", string.Join(" · ", path.Steps.Skip(index + 1).Select(later => later.Label)), string.Empty)
                        ]),
                        path.Id);
                }
            }

            var unguarded = analysis.Facts.FailurePaths.FirstOrDefault(failure =>
                failure.Kind == "catch" && !failure.Guarded && failure.Sites.Any(site => site.CalleeNodeId is not null));
            if (unguarded is not null && path.Steps.Any(step => step.Compensation is CompensationKind.Yes or CompensationKind.PartialPaths))
            {
                Add(new Finding(
                    NextId(findings.Count), "COMP-003", FindingSeverity.Warning, FindingConfidence.Estimated,
                    "보상 호출이 try 없이 실행된다",
                    "되돌리는 호출이 다시 실패하면 원본 예외까지 덮인다. 되돌아갔는지도 알 수 없다.",
                    "보상 호출을 try로 감싸고 실패를 별도로 남긴다.",
                    path.MethodDisplayName, path.File, unguarded.Line, path.MethodNodeId, path.Id, null, null,
                    [Evidence("bad", "catch 안의 보상 호출", string.Join(" · ", unguarded.Sites.Select(site => site.Name)), $"{path.File}:{unguarded.Line}")]),
                    path.Id);
            }
        }

        if (startupFlow is not null)
        {
            foreach (var step in EnumerateSteps(startupFlow.ServiceSteps))
            {
                if (step.Lifetime != StartupLifetime.Singleton || step.ImplementationType is null)
                {
                    continue;
                }

                if (!LooksLikeConnectionType(step.ImplementationType) && !LooksLikeConnectionType(step.ServiceType))
                {
                    continue;
                }

                Add(new Finding(
                    NextId(findings.Count), "CONN-002", FindingSeverity.Critical, FindingConfidence.Certain,
                    $"{step.ImplementationType}이(가) Singleton으로 등록되어 있다",
                    "DbContext·커넥션은 스레드 안전하지 않다. 동시 요청이 한 인스턴스를 공유하면 예외와 데이터 섞임이 생긴다.",
                    "AddScoped로 바꾸고, 백그라운드 작업에서는 IServiceScopeFactory로 스코프를 만든다.",
                    step.Label, step.File, step.Line, null, null, null, null,
                    [Evidence("bad", "DI 등록", step.Label, $"{step.File}:{step.Line}")]));
            }
        }

        var dataSources = BuildDataSources(scan, options);
        var document = new DataAccessDocument(
            sharding,
            dataSources,
            scan.Accesses,
            scan.Transactions,
            writePaths.Select(analysis => analysis.Path with
            {
                FindingIds = pathFindings.TryGetValue(analysis.Path.Id, out var ids) ? ids : []
            }).ToArray(),
            findings,
            BuildRuleCatalog(sharding))
        {
            Notes = BuildNotes(scan, options)
        };

        logger.LogInformation(
            "DB·캐시 점검 {Count}건(치명 {Critical} · 경고 {Warning} · 정보 {Info}).",
            findings.Count,
            findings.Count(item => item.Severity == FindingSeverity.Critical),
            findings.Count(item => item.Severity == FindingSeverity.Warning),
            findings.Count(item => item.Severity == FindingSeverity.Info));
        return document;
    }

    private static string ResolveDataSource(MethodDataFacts facts, DataCallSite site)
    {
        if (site.DataSource.Length > 0)
        {
            return site.DataSource;
        }

        var connection = facts.Sites
            .LastOrDefault(other => other.Kind == SiteKind.ConnectionOpen && other.Position < site.Position);
        return connection?.DataSource ?? "Db";
    }

    private static bool LooksLikeShardKey(string identifier, DataAccessOptions options) =>
        options.ShardKeyParameters.Any(name => identifier.Contains(name, StringComparison.OrdinalIgnoreCase)) ||
        identifier.EndsWith("No", StringComparison.Ordinal) ||
        identifier.EndsWith("Id", StringComparison.Ordinal);

    /// <summary>
    /// 커넥션·DbContext처럼 요청마다 새로 만들어야 하는 타입인지. 팩토리·풀·옵션은 Singleton이 정상이라 뺀다.
    /// </summary>
    private static bool LooksLikeConnectionType(string? typeName) => typeName is not null &&
        !typeName.Contains("Factory", StringComparison.OrdinalIgnoreCase) &&
        !typeName.Contains("Pool", StringComparison.OrdinalIgnoreCase) &&
        !typeName.Contains("Options", StringComparison.OrdinalIgnoreCase) &&
        (typeName.Contains("DbContext", StringComparison.OrdinalIgnoreCase) ||
         typeName.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
         typeName.Contains("UnitOfWork", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<StartupStep> EnumerateSteps(IReadOnlyList<StartupStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var child in EnumerateSteps(step.Children))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// 인식했지만 해석하지 않은 입력을 사실로 남긴다.
    /// 규칙 결과는 손대지 않는다 — 규칙은 실제로 돌았고, 그 입력을 읽지 않았을 뿐이다.
    ///
    /// ORM 힌트가 있으면 접근이 1건 이상이어도 <b>항상</b> 남긴다.
    /// 예전에는 접근 0건일 때만 남겨서, Dapper 접근 1건이 있으면 EF Core `DbSet` 100곳이 있어도
    /// 아무 말 없이 "규칙을 다 검사했다"로 읽혔다(T55).
    /// </summary>
    private static IReadOnlyList<string> BuildNotes(DataAccessScanResult scan, DataAccessOptions options)
    {
        var notes = new List<string>();
        if (scan.OrmTypeHints.Count > 0)
        {
            var described = scan.OrmTypeHints
                .Select(hint => scan.OrmTypeHintCounts.TryGetValue(hint, out var count)
                    ? $"{hint} {count}곳"
                    : hint)
                .ToArray();
            notes.Add(
                $"솔루션에 {string.Join(", ", described)}을(를) 참조하는 자리가 있지만 접근으로 세지 않았다. " +
                "지금 버전은 SQL 리터럴·Dapper·SqlKata·설정한 queryApis만 읽는다(외부 네임스페이스 집계 참고).");
        }

        if (scan.Accesses.Count > 0)
        {
            return notes;
        }

        if (options.QueryApis.SequenceEqual(DataAccessOptions.Default.QueryApis, StringComparer.Ordinal))
        {
            notes.Add(
                "db.queryApis가 기본값이다. 사내 데이터 래퍼를 쓴다면 `tracelens init`으로 설정 파일을 만들고 " +
                "db.queryApis에 그 API 패턴을 적어야 접근을 읽는다.");
        }

        return notes;
    }

    private static IReadOnlyList<DataSourceInfo> BuildDataSources(DataAccessScanResult scan, DataAccessOptions options)
    {
        var counts = scan.Accesses
            .GroupBy(access => access.DataSource, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var names = counts.Keys
            .Concat(scan.DataSourceKinds.Keys)
            .Concat(options.DataSources.Select(source => source.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        return names.Select(name =>
        {
            var configured = options.DataSources.FirstOrDefault(source =>
                string.Equals(source.Name, name, StringComparison.OrdinalIgnoreCase));
            var kind = configured?.Kind ?? (scan.DataSourceKinds.TryGetValue(name, out var scanned) ? scanned : "single");
            var description = kind switch
            {
                "shard" => $"샤드 {configured?.ShardCount ?? 0}개",
                "cache" => "캐시 · 락",
                _ => configured?.ReadOnly == true ? "읽기 전용" : configured?.WriteOnly == true ? "쓰기 전용" : "단일"
            };
            return new DataSourceInfo(
                name,
                kind,
                configured?.ShardCount ?? 0,
                configured?.ReadOnly ?? false,
                configured?.WriteOnly ?? false,
                description,
                counts.TryGetValue(name, out var count) ? count : 0);
        }).ToArray();
    }

    private static Finding Create(
        string ruleId,
        FindingConfidence confidence,
        MethodDataFacts facts,
        DataCallSite site,
        string what,
        string why,
        string fix,
        IReadOnlyList<FindingEvidence> evidence) => new(
        $"{ruleId}:{facts.NodeId}:{site.Line}",
        ruleId,
        Catalog.First(rule => rule.Id == ruleId).Severity,
        confidence,
        what,
        why,
        fix,
        facts.DisplayName,
        facts.File,
        site.Line,
        facts.NodeId,
        null,
        null,
        site.Target.Length > 0 ? site.Target : null,
        evidence);

    private static FindingEvidence Evidence(string tone, string head, string detail, string note) =>
        new(tone, head, detail, note);

    private static string NextId(int index) => $"f{index + 1}";

    private static IReadOnlyList<RuleInfo> BuildRuleCatalog(bool sharding) => Catalog
        .Select(rule => rule.ShardOnly && !sharding ? rule with { Enabled = false } : rule)
        .Select(rule => rule with { Scope = ScopeOf(rule.Id) })
        .ToArray();

    /// <summary>
    /// 규칙이 무엇을 보고 판정하는지. 규칙군으로 정한다 —
    /// "검사했는데 안 걸렸다"와 "그 입력을 읽지 않았다"를 화면에서 구분하기 위한 값이다.
    /// </summary>
    private static RuleScope ScopeOf(string ruleId) => ruleId switch
    {
        _ when ruleId.StartsWith("CACHE-", StringComparison.Ordinal) => RuleScope.CacheApis,
        "CONN-002" => RuleScope.StartupRegistrations,
        _ => RuleScope.SqlAndConfiguredApis
    };

    /// <summary>
    /// 규칙 카탈로그. <c>Enabled == false</c>인 규칙은 이번 단계에서 검사하지 않는 것이고, 화면에 "꺼짐"으로 나온다.
    /// </summary>
    public static IReadOnlyList<RuleInfo> Catalog { get; } =
    [
        new("SHARD-001", "샤딩", "샤드 키 없이 전 샤드 순회", FindingSeverity.Warning, FindingConfidence.Certain,
            "반복문 안에서 커넥션을 열어 모든 샤드에 같은 쿼리를 실행한다. 집계라면 의도일 수 있다.", true, true),
        new("SHARD-002", "샤딩", "커넥션을 연 유저와 쿼리 대상 유저가 다름", FindingSeverity.Critical, FindingConfidence.Estimated,
            "userNo A로 커넥션을 열고 파라미터로 다른 번호를 넘긴다. 샤드가 다르면 그 데이터는 이 커넥션에 없다.", true, true),
        new("SHARD-004", "샤딩", "하드코딩된 샤드 번호", FindingSeverity.Warning, FindingConfidence.Certain,
            "상수로 샤드 번호를 넘긴다. 운영 도구가 아니면 대부분 실수다.", true, true),
        new("SHARD-005", "샤딩", "샤드 키 출처를 검증하지 않음", FindingSeverity.Critical, FindingConfidence.Estimated,
            "요청 본문·쿼리스트링에서 온 값을 인증 정보와 대조하지 않고 그대로 쓴다.", true, true),

        new("TX-001", "트랜잭션", "트랜잭션 안에서 외부 호출", FindingSeverity.Warning, FindingConfidence.Certain,
            "커밋 전에 HTTP 같은 외부 호출을 기다린다. 락 유지 시간이 외부 응답에 묶인다.", false, true),
        new("TX-002", "트랜잭션", "커밋/롤백이 없는 경로", FindingSeverity.Warning, FindingConfidence.Estimated,
            "예외 경로에서 커밋도 롤백도 없이 스코프를 벗어난다.", false, false),
        new("TX-003", "트랜잭션", "한 트랜잭션이 여러 커넥션에 걸침", FindingSeverity.Critical, FindingConfidence.Certain,
            "경계 안에서 다른 커넥션이 열린다. 분산 트랜잭션이 없으면 한쪽만 반영될 수 있다.", false, true),

        new("COMP-001", "보상", "트랜잭션 없이 쓰기 2개 이상", FindingSeverity.Info, FindingConfidence.Certain,
            "묶을 수 있는 쓰기가 흩어져 있다. 보상 처리를 살펴야 하는 경로의 목록이 된다.", false, true),
        new("COMP-002", "보상", "실패 시 되돌리지 않는 선행 쓰기", FindingSeverity.Critical, FindingConfidence.Certain,
            "뒤 단계에서 실패하면 앞 단계가 그대로 남는다. 실패 경로에서 역연산을 찾지 못했다.", false, true),
        new("COMP-003", "보상", "보상 실패를 무시하거나 예외로 덮음", FindingSeverity.Warning, FindingConfidence.Estimated,
            "되돌리는 호출이 try 없이 실행된다. 보상 중 예외가 나면 원본 예외까지 덮인다.", false, true),
        new("COMP-004", "보상", "보상 순서가 역순이 아님", FindingSeverity.Info, FindingConfidence.Estimated,
            "되돌리기는 마지막에 한 것부터 푸는 것이 안전하다.", false, false),
        new("COMP-005", "보상", "되돌릴 수 없는 작업 뒤에 실패 가능한 쓰기", FindingSeverity.Warning, FindingConfidence.Certain,
            "푸시·결제처럼 취소가 불가능한 작업을 먼저 하고 그 뒤에 실패할 수 있는 쓰기를 한다.", false, true),
        new("COMP-006", "보상", "조기 반환 경로에 보상이 없음", FindingSeverity.Critical, FindingConfidence.Certain,
            "catch에는 되돌리기가 있지만 값으로 빠져나가는 경로에는 없다.", false, true),

        new("CACHE-001", "캐시", "DB 쓰기 후 캐시 무효화 없음", FindingSeverity.Warning, FindingConfidence.Estimated,
            "같은 데이터를 담는 캐시 키를 읽는 곳이 있는데 쓰기 경로에 무효화가 없다.", false, false),
        new("CACHE-002", "캐시", "캐시를 먼저 갱신하고 DB 실패를 되돌리지 않음", FindingSeverity.Critical, FindingConfidence.Certain,
            "캐시가 먼저 바뀌고 뒤따르는 DB 쓰기가 실패하면 둘이 어긋난 채로 남는다.", false, true),
        new("CACHE-003", "캐시", "TTL 없이 SET", FindingSeverity.Warning, FindingConfidence.Estimated,
            "만료가 없는 키가 쌓인다. 락이면 영구 잠김이 된다. 키·만료를 이 자리에서 확정할 수 없는 호출(솔루션 안 캐시 래퍼 경유)은 검사하지 않는다.", false, true),
        new("CACHE-004", "캐시", "KEYS · FLUSH 등 전체 순회 명령", FindingSeverity.Critical, FindingConfidence.Certain,
            "운영 Redis를 블로킹한다.", false, true),
        new("CACHE-005", "캐시", "반복문 안에서 캐시 읽기", FindingSeverity.Warning, FindingConfidence.Certain,
            "왕복이 반복 횟수만큼 늘어난다. MGET으로 묶을 자리다.", false, true),
        new("CACHE-006", "캐시", "분산 락 해제가 빠진 경로", FindingSeverity.Critical, FindingConfidence.Estimated,
            "SET NX로 잡은 락을 예외 경로에서 풀지 않는다.", false, false),
        new("CACHE-007", "캐시", "트랜잭션 안에서 캐시 호출", FindingSeverity.Warning, FindingConfidence.Certain,
            "DB 트랜잭션이 열린 채로 캐시 왕복을 기다린다.", false, true),
        new("CACHE-008", "캐시", "키 이름 규칙이 제각각", FindingSeverity.Info, FindingConfidence.Estimated,
            "같은 데이터를 가리키는 키가 두 가지 형태로 만들어진다.", false, false),

        new("CONN-001", "커넥션", "using 없이 연 커넥션", FindingSeverity.Warning, FindingConfidence.Estimated,
            "IDisposable을 using 없이 열고 Dispose 호출도 보이지 않는다.", false, true),
        new("CONN-002", "커넥션", "DbContext·커넥션이 Singleton 등록", FindingSeverity.Critical, FindingConfidence.Certain,
            "시작 흐름에서 읽은 DI 등록을 그대로 본다. 동시 요청이 한 인스턴스를 공유한다.", false, true),

        new("RW-001", "읽기·쓰기", "읽기 전용 데이터소스에 쓰기", FindingSeverity.Critical, FindingConfidence.Certain,
            "설정에서 readOnly로 표시한 데이터소스에 INSERT/UPDATE/DELETE를 실행한다.", false, true),
        new("RW-002", "읽기·쓰기", "단순 조회를 마스터로", FindingSeverity.Info, FindingConfidence.Estimated,
            "복제본으로 옮길 수 있는 읽기 전용 경로를 알려준다.", false, false),

        new("PERF-001", "성능", "반복문 안 쿼리(N+1)", FindingSeverity.Warning, FindingConfidence.Certain,
            "반복문 안에서 쿼리를 실행한다.", false, true),
        new("PERF-002", "성능", "비동기 API를 .Result/.Wait()로 호출", FindingSeverity.Warning, FindingConfidence.Certain,
            "스레드풀을 막는다. 부하가 걸리면 커넥션 고갈로 이어진다.", false, true),

        new("SEC-001", "스키마·보안", "문자열 결합으로 만든 SQL", FindingSeverity.Critical, FindingConfidence.Certain,
            "파라미터가 아닌 문자열 결합·보간으로 SQL을 만든다. 빌더 체인에서 합성한 문장은 원문 SQL이 아니라 검사하지 않는다.", false, true),
        new("IDX-001", "스키마·보안", "인덱스 없는 컬럼으로 조회", FindingSeverity.Info, FindingConfidence.Estimated,
            "schema.sql을 함께 읽을 때만 검사할 수 있다.", false, false),
        new("MAP-001", "스키마·보안", "프로시저 파라미터 불일치", FindingSeverity.Info, FindingConfidence.Estimated,
            "DDL의 프로시저 정의와 코드가 넘기는 파라미터가 다르다.", false, false)
    ];
}

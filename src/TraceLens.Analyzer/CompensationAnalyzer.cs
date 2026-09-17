using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <param name="Storage">이 대상이 DB인지 캐시인지 외부 호출인지.</param>
public sealed record EffectTarget(StorageKind Storage, string DataSource, string Target, AccessOperation Operation);

/// <summary>메서드를 부르면 결국 무엇을 읽고 쓰는지. 원시 접근을 호출 그래프를 따라 전파해 만든다.</summary>
public sealed record MethodEffect(IReadOnlyList<EffectTarget> Writes, IReadOnlyList<EffectTarget> Reads)
{
    public static MethodEffect Empty { get; } = new([], []);

    public bool HasWrite => Writes.Count > 0;
}

/// <param name="Path">만들어진 쓰기 경로.</param>
/// <param name="Sites">단계마다 대응하는 호출 자리. 규칙 엔진이 근거를 만들 때 쓴다.</param>
public sealed record WritePathAnalysis(WritePath Path, MethodDataFacts Facts, IReadOnlyList<DataCallSite> Sites);

/// <summary>
/// 트랜잭션으로 묶이지 않은 쓰기 경로를 찾고, 각 단계가 실패 시 되돌려지는지 판정한다.
/// 되돌리기는 실패 경로(<c>catch</c>·조기 <c>return</c>) 안에서 역연산 호출을 찾는 방식으로 확인한다.
/// </summary>
public sealed class CompensationAnalyzer(ILogger<CompensationAnalyzer> logger)
{
    /// <summary>접근 요약을 위로 전파하는 최대 반복 수. 순환이 있어도 여기서 멈춘다.</summary>
    private const int MaxPropagationPasses = 8;

    public IReadOnlyDictionary<string, MethodEffect> BuildEffects(
        IReadOnlyList<MethodDataFacts> methods,
        IReadOnlyDictionary<string, string> methodDataSources,
        CancellationToken cancellationToken = default)
    {
        var effects = new Dictionary<string, MethodEffect>(StringComparer.Ordinal);
        foreach (var facts in methods)
        {
            var writes = new List<EffectTarget>();
            var reads = new List<EffectTarget>();
            foreach (var site in facts.Sites)
            {
                if (site.Kind is not (SiteKind.Db or SiteKind.Cache or SiteKind.External))
                {
                    continue;
                }

                var storage = site.Kind switch
                {
                    SiteKind.Cache => StorageKind.Cache,
                    SiteKind.External => StorageKind.External,
                    _ => StorageKind.Database
                };
                var dataSource = site.DataSource.Length > 0
                    ? site.DataSource
                    : methodDataSources.TryGetValue(facts.NodeId, out var owner) ? owner : string.Empty;
                var target = new EffectTarget(
                    storage,
                    dataSource,
                    site.Target.Length > 0 ? site.Target : site.Name,
                    site.Operation);
                (site.IsWrite ? writes : reads).Add(target);
            }

            effects[facts.NodeId] = new MethodEffect(writes, reads);
        }

        // 서비스는 SQL을 직접 쓰지 않는다. 호출한 메서드의 요약을 위로 끌어올려야 "이 호출이 쓰기"인지 알 수 있다.
        for (var pass = 0; pass < MaxPropagationPasses; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = false;
            foreach (var facts in methods)
            {
                var current = effects[facts.NodeId];
                var writes = current.Writes.ToList();
                var reads = current.Reads.ToList();
                foreach (var site in facts.Sites)
                {
                    if (site.CalleeNodeId is null ||
                        site.CalleeNodeId == facts.NodeId ||
                        !effects.TryGetValue(site.CalleeNodeId, out var callee))
                    {
                        continue;
                    }

                    changed |= Merge(writes, callee.Writes);
                    changed |= Merge(reads, callee.Reads);
                }

                if (writes.Count != current.Writes.Count || reads.Count != current.Reads.Count)
                {
                    effects[facts.NodeId] = new MethodEffect(writes, reads);
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return effects;
    }

    public IReadOnlyList<WritePathAnalysis> BuildWritePaths(
        IReadOnlyList<MethodDataFacts> methods,
        IReadOnlyDictionary<string, MethodEffect> effects,
        IReadOnlyDictionary<string, string> methodDataSources,
        TraceLensConfig config,
        CancellationToken cancellationToken = default)
    {
        var result = new List<WritePathAnalysis>();
        foreach (var facts in methods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steps = CollectWriteSites(facts, effects);
            if (steps.Count < 2)
            {
                continue;
            }

            var writeSteps = new List<WriteStep>();
            for (var index = 0; index < steps.Count; index++)
            {
                writeSteps.Add(BuildStep(index + 1, steps[index], facts, effects, methodDataSources, config));
            }

            // 캐시 무효화처럼 되돌릴 필요가 없는 단계가 섞여 있어도, 실제 쓰기가 모두 트랜잭션 안이면 안전한 경로다.
            var inTransaction =
                writeSteps.Any(step => step.Compensation == CompensationKind.Transaction) &&
                writeSteps.All(step => step.Compensation is CompensationKind.Transaction or CompensationKind.NotNeeded);
            var note = inTransaction
                ? "쓰기 전부가 한 트랜잭션 안에 있다 — 실패하면 함께 롤백된다."
                : facts.FailurePaths.Count == 0
                    ? "트랜잭션도 실패 처리도 없다. 중간에 예외가 나면 앞 단계가 그대로 남는다."
                    : $"트랜잭션 없이 {steps.Count}단계를 순서대로 바꾼다. 실패 경로 {facts.FailurePaths.Count}곳을 확인했다.";

            result.Add(new WritePathAnalysis(
                new WritePath(
                    $"path:{facts.NodeId}",
                    facts.NodeId,
                    facts.DisplayName,
                    facts.ClassId,
                    facts.File,
                    facts.StartLine,
                    inTransaction,
                    note,
                    writeSteps,
                    []),
                facts,
                steps));
        }

        logger.LogInformation("쓰기 경로 {Count}개를 찾았다(트랜잭션 없는 경로 {Unsafe}개).",
            result.Count,
            result.Count(item => !item.Path.InTransaction));
        return result;
    }

    /// <summary>메서드 본문에서 상태를 바꾸는 호출만 소스 순서로 고른다. 실패 처리 경로 안의 호출은 단계가 아니라 보상이다.</summary>
    private static IReadOnlyList<DataCallSite> CollectWriteSites(
        MethodDataFacts facts, IReadOnlyDictionary<string, MethodEffect> effects)
    {
        var inFailurePath = facts.FailurePaths
            .SelectMany(path => path.Sites)
            .Select(site => site.Position)
            .ToHashSet();

        var sites = new List<DataCallSite>();
        foreach (var site in facts.Sites)
        {
            if (inFailurePath.Contains(site.Position) || site.InCatch)
            {
                continue;
            }

            var isWrite = site.Kind switch
            {
                SiteKind.Db or SiteKind.Cache => site.IsWrite,
                SiteKind.External => true,
                SiteKind.Plain => site.CalleeNodeId is not null &&
                                  effects.TryGetValue(site.CalleeNodeId, out var effect) && effect.HasWrite,
                _ => false
            };
            if (isWrite)
            {
                sites.Add(site);
            }
        }

        return sites;
    }

    private static WriteStep BuildStep(
        int order,
        DataCallSite site,
        MethodDataFacts facts,
        IReadOnlyDictionary<string, MethodEffect> effects,
        IReadOnlyDictionary<string, string> methodDataSources,
        TraceLensConfig config)
    {
        var effect = site.CalleeNodeId is not null && effects.TryGetValue(site.CalleeNodeId, out var callee)
            ? callee
            : MethodEffect.Empty;
        var target = site.Target.Length > 0
            ? site.Target
            : effect.Writes.FirstOrDefault()?.Target ?? site.Name;
        var storage = site.Kind switch
        {
            SiteKind.Cache => StorageKind.Cache,
            SiteKind.External => StorageKind.External,
            SiteKind.Db => StorageKind.Database,
            _ => effect.Writes.FirstOrDefault()?.Storage ?? StorageKind.Database
        };
        var dataSource = site.DataSource.Length > 0
            ? site.DataSource
            : effect.Writes.FirstOrDefault(item => item.DataSource.Length > 0)?.DataSource
              ?? (site.CalleeNodeId is not null && methodDataSources.TryGetValue(site.CalleeNodeId, out var owner)
                  ? owner
                  : string.Empty);
        var operation = site.Kind is SiteKind.Db or SiteKind.Cache
            ? site.Operation
            : effect.Writes.FirstOrDefault()?.Operation ?? AccessOperation.Write;

        var label = storage == StorageKind.Cache
            ? $"{site.Statement}".Trim()
            : target.Length > 0 && site.Kind != SiteKind.Plain
                ? $"{target} {DescribeOperation(operation)}"
                : site.Name;
        // 래퍼 호출은 이 줄에 직접 SQL이 없다. 어느 메서드를 경유해 저장소에 닿는지를 그대로 적는다.
        var detail = site.Kind == SiteKind.Plain
            ? $"{site.ReceiverText}.{site.Name} 경유 · {(dataSource.Length > 0 ? dataSource : "저장소")}{(target.Length > 0 ? $" · {target}" : string.Empty)}"
            : $"{site.Statement}";

        var (compensation, note) = Classify(site, facts, config, storage, operation, target, dataSource);
        return new WriteStep(
            order,
            label.Length > 0 ? label : site.Name,
            detail,
            storage,
            dataSource,
            target,
            operation,
            compensation,
            note,
            facts.File,
            site.Line);
    }

    private static (CompensationKind Kind, string Note) Classify(
        DataCallSite site,
        MethodDataFacts facts,
        TraceLensConfig config,
        StorageKind storage,
        AccessOperation operation,
        string target,
        string dataSource)
    {
        var compensation = config.Db.Compensation;

        // 외부 발송은 트랜잭션과 무관하게 되돌릴 수 없다. 그래서 트랜잭션 판정보다 먼저 본다.
        if (storage == StorageKind.External || PatternMatch.MatchesAny(site.CalleeSymbol, compensation.Irreversible))
        {
            return (CompensationKind.Irreversible, "취소 수단이 없는 호출이라 트랜잭션으로도 되돌아가지 않는다.");
        }

        if (site.InTransaction)
        {
            return site.TransactionWrapper is { Length: > 0 } wrapper
                ? (CompensationKind.Transaction, $"트랜잭션 래퍼 {wrapper} 안이라 실패하면 함께 롤백된다.")
                : (CompensationKind.Transaction, "트랜잭션 안이라 실패하면 함께 롤백된다.");
        }

        if (PatternMatch.MatchesAny(site.CalleeSymbol, compensation.Idempotent))
        {
            return (CompensationKind.NotNeeded, "설정의 idempotent 목록에 있다 — 재실행해도 안전하다.");
        }

        if (storage == StorageKind.Cache && operation == AccessOperation.Delete)
        {
            return (CompensationKind.NotNeeded, "캐시 무효화라 재실행해도 안전하다 — 되돌릴 필요가 없다.");
        }

        if (IsLogWrite(site, dataSource))
        {
            return (CompensationKind.NotNeeded, "로그성 쓰기라 남아도 무해하다.");
        }

        var relevant = facts.FailurePaths.Where(path => path.Position > site.Position).ToArray();
        if (relevant.Length == 0)
        {
            return (CompensationKind.No, "이 단계 뒤에 실패를 처리하는 경로가 없다 — 실패하면 그대로 남는다.");
        }

        var candidates = UndoCandidates(site, config).ToArray();
        var matched = relevant.Where(path => path.Sites.Any(undo => IsUndo(undo, candidates, site, target))).ToArray();
        if (matched.Length == relevant.Length)
        {
            var where = matched[0].Kind == "catch" ? "catch" : "조기 반환 경로";
            return (CompensationKind.Yes, $"실패 경로 {relevant.Length}곳 모두에서 역연산을 찾았다({where} {matched[0].Line}줄).");
        }

        if (matched.Length > 0)
        {
            var missing = relevant.First(path => !matched.Contains(path));
            return (CompensationKind.PartialPaths,
                $"{matched[0].Kind} 경로에는 역연산이 있지만 {DescribePath(missing)}({missing.Line}줄)에는 없다.");
        }

        return (CompensationKind.No,
            $"실패 경로 {relevant.Length}곳을 훑었지만 역연산({string.Join(" · ", candidates.Take(3))})을 찾지 못했다.");
    }

    private static string DescribePath(FailurePath path) => path.Kind == "catch" ? "catch 경로" : "조기 반환 경로";

    private static string DescribeOperation(AccessOperation operation) => operation switch
    {
        AccessOperation.Write => "쓰기",
        AccessOperation.Delete => "삭제",
        AccessOperation.Read => "읽기",
        _ => "접근"
    };

    /// <summary>
    /// 로그성 쓰기인지. 테이블 이름에 log가 들어간다고 로그로 보면 <c>attendance_log</c> 같은 실제 데이터까지 놓치므로
    /// 호출 이름이나 데이터소스 이름으로만 판단한다.
    /// </summary>
    private static bool IsLogWrite(DataCallSite site, string dataSource) =>
        site.Name.Contains("Log", StringComparison.OrdinalIgnoreCase) ||
        dataSource.Contains("Log", StringComparison.OrdinalIgnoreCase);

    /// <summary>이 단계를 되돌릴 만한 메서드 이름 후보. 설정에 적은 짝이 이름 규칙보다 먼저다.</summary>
    private static IEnumerable<string> UndoCandidates(DataCallSite site, TraceLensConfig config)
    {
        var name = Trim(site.Name);
        foreach (var pair in config.Db.Compensation.Pairs)
        {
            var doName = Trim(pair.Do);
            if (string.Equals(name, doName, StringComparison.OrdinalIgnoreCase))
            {
                yield return Trim(pair.Undo);
            }
            else if (name.StartsWith(doName, StringComparison.OrdinalIgnoreCase))
            {
                yield return Trim(pair.Undo) + name[doName.Length..];
            }
        }

        foreach (var pair in CompensationOptions.NameHeuristics)
        {
            if (name.StartsWith(pair.Do, StringComparison.OrdinalIgnoreCase))
            {
                yield return pair.Undo + name[pair.Do.Length..];
            }
        }

        static string Trim(string value) =>
            value.EndsWith("Async", StringComparison.Ordinal) ? value[..^5] : value;
    }

    private static bool IsUndo(DataCallSite candidate, IReadOnlyList<string> names, DataCallSite step, string target)
    {
        var candidateName = candidate.Name.EndsWith("Async", StringComparison.Ordinal)
            ? candidate.Name[..^5]
            : candidate.Name;
        if (names.Any(name => string.Equals(name, candidateName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 캐시는 이름이 아니라 "같은 키를 지우는가"로 본다.
        return step.Kind == SiteKind.Cache &&
               candidate.Kind == SiteKind.Cache &&
               candidate.Operation == AccessOperation.Delete &&
               target.Length > 0 &&
               string.Equals(candidate.Target, target, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Merge(List<EffectTarget> into, IReadOnlyList<EffectTarget> from)
    {
        var changed = false;
        foreach (var item in from)
        {
            if (into.Any(existing => existing.Storage == item.Storage &&
                                     existing.Target == item.Target &&
                                     existing.Operation == item.Operation &&
                                     existing.DataSource == item.DataSource))
            {
                continue;
            }

            into.Add(item);
            changed = true;
        }

        return changed;
    }
}

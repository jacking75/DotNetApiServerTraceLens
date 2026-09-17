using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

public enum DiffKind
{
    Added,
    Removed,
    Changed,
    Unchanged,
    NotComparable
}

/// <param name="Detail">무엇이 달라졌는지(시그니처·본문·해석 근거 등).</param>
public sealed record DiffEntry(string Id, string Label, DiffKind Kind, string Detail)
{
    public string File { get; init; } = string.Empty;

    public int Line { get; init; }
}

/// <param name="Ok">비교 조건이 같은지. false면 결과를 "수정 완료"의 근거로 쓰면 안 된다.</param>
public sealed record ComparePreconditions(bool Ok, IReadOnlyList<string> Differences, IReadOnlyList<string> Notes);

public sealed record SnapshotDiff(
    string BaselineId,
    string CurrentId,
    DateTimeOffset BaselineAnalyzedAt,
    DateTimeOffset CurrentAnalyzedAt,
    ComparePreconditions Preconditions,
    IReadOnlyList<DiffEntry> Endpoints,
    IReadOnlyList<DiffEntry> Methods,
    IReadOnlyList<DiffEntry> Edges,
    IReadOnlyList<DiffEntry> DataAccess,
    IReadOnlyList<DiffEntry> Findings,
    IReadOnlyList<DiffEntry> RenameCandidates,
    IReadOnlyList<string> Limitations)
{
    public DiffCounts Counts => new(
        Endpoints.Count(item => item.Kind == DiffKind.Added),
        Endpoints.Count(item => item.Kind == DiffKind.Removed),
        Methods.Count(item => item.Kind == DiffKind.Added),
        Methods.Count(item => item.Kind == DiffKind.Removed),
        Methods.Count(item => item.Kind == DiffKind.Changed),
        Edges.Count(item => item.Kind == DiffKind.Added),
        Edges.Count(item => item.Kind == DiffKind.Removed),
        Findings.Count(item => item.Kind == DiffKind.Added),
        Findings.Count(item => item.Kind == DiffKind.Removed));
}

public sealed record DiffCounts(
    int EndpointsAdded,
    int EndpointsRemoved,
    int MethodsAdded,
    int MethodsRemoved,
    int MethodsChanged,
    int EdgesAdded,
    int EdgesRemoved,
    int FindingsAdded,
    int FindingsResolved);

/// <summary>
/// 두 분석 스냅샷의 구조 차이를 계산한다.
/// 이름 변경은 원칙적으로 추가·삭제로 처리하고, 본문·구조가 비슷한 쌍만 "이름 변경 후보"로 따로 제시한다(자동 확정하지 않는다).
/// "차이 없음"은 "안전"이나 "테스트 통과"를 뜻하지 않는다.
/// </summary>
public sealed class SnapshotComparer
{
    public SnapshotDiff Compare(GraphDocument baseline, GraphDocument current)
    {
        var preconditions = CheckPreconditions(baseline, current);
        var limitations = new List<string>(preconditions.Notes)
        {
            "\"차이 없음\"은 안전하다는 뜻도, 테스트가 통과한다는 뜻도 아니다.",
            "본문 지문은 서식·주석 변경과 실제 변경을 구분할 뿐이며 의미적 동등성의 증명이 아니다."
        };

        var endpoints = CompareEndpoints(baseline, current);
        var (methods, renameCandidates) = CompareMethods(baseline, current, limitations);
        var edges = CompareEdges(baseline, current);
        var dataAccess = CompareDataAccess(baseline, current);
        var findings = CompareFindings(baseline, current, limitations);

        return new SnapshotDiff(
            baseline.SnapshotId,
            current.SnapshotId,
            baseline.AnalyzedAt,
            current.AnalyzedAt,
            preconditions,
            endpoints,
            methods,
            edges,
            dataAccess,
            findings,
            renameCandidates,
            limitations);
    }

    /// <summary>같은 대상·설정·분석기·규칙·분석 범위인지 확인한다. 다르면 비교 조건이 다르다고 표시한다.</summary>
    public static ComparePreconditions CheckPreconditions(GraphDocument baseline, GraphDocument current)
    {
        var differences = new List<string>();
        var notes = new List<string>();

        if (!string.Equals(baseline.SolutionPath, current.SolutionPath, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"분석 대상이 다르다: {baseline.SolutionPath} → {current.SolutionPath}");
        }

        if (!string.Equals(baseline.AnalyzerVersion, current.AnalyzerVersion, StringComparison.Ordinal))
        {
            differences.Add($"분석기 버전이 다르다: {Display(baseline.AnalyzerVersion)} → {Display(current.AnalyzerVersion)}");
        }

        if (!string.Equals(baseline.RulesVersion, current.RulesVersion, StringComparison.Ordinal))
        {
            differences.Add($"규칙 버전이 다르다: {Display(baseline.RulesVersion)} → {Display(current.RulesVersion)}");
        }

        var baselineScope = baseline.Diagnostics?.AnalysisScope ?? "unknown";
        var currentScope = current.Diagnostics?.AnalysisScope ?? "unknown";
        if (!string.Equals(baselineScope, currentScope, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"분석 범위가 다르다: {baselineScope} → {currentScope}");
        }

        if (baseline.SchemaVersion < 7 || current.SchemaVersion < 7)
        {
            notes.Add("한쪽이 schema v6 이하라 본문 지문이 없다. 메서드 비교를 추가·삭제로만 한다.");
        }

        if ((baseline.Diagnostics?.ProjectsWithCompileErrors ?? 0) != (current.Diagnostics?.ProjectsWithCompileErrors ?? 0))
        {
            notes.Add("컴파일 오류가 있는 프로젝트 수가 달라졌다. 프로젝트 로드 실패로 사라진 점검을 \"수정 완료\"로 보면 안 된다.");
        }

        return new ComparePreconditions(differences.Count == 0, differences, notes);
    }

    private static IReadOnlyList<DiffEntry> CompareEndpoints(GraphDocument baseline, GraphDocument current)
    {
        // 가져온 파일에 같은 엔드포인트 키가 두 번 있을 수 있다. ToDictionary는 그럴 때 예외를 던진다.
        var baselineMap = baseline.EntryPoints
            .GroupBy(GraphIndex.EndpointKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var currentMap = current.EntryPoints
            .GroupBy(GraphIndex.EndpointKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var result = new List<DiffEntry>();

        foreach (var (key, item) in currentMap)
        {
            if (!baselineMap.TryGetValue(key, out var before))
            {
                result.Add(new DiffEntry(key, $"{item.HttpMethod} {item.Route}", DiffKind.Added, "새 엔드포인트다.")
                {
                    File = item.RegistrationFile,
                    Line = item.RegistrationLine
                });
                continue;
            }

            if (!string.Equals(before.NodeId, item.NodeId, StringComparison.Ordinal))
            {
                result.Add(new DiffEntry(
                    key,
                    $"{item.HttpMethod} {item.Route}",
                    DiffKind.Changed,
                    $"핸들러가 바뀌었다: {before.NodeId} → {item.NodeId}")
                {
                    File = item.RegistrationFile,
                    Line = item.RegistrationLine
                });
            }
        }

        foreach (var (key, item) in baselineMap.Where(entry => !currentMap.ContainsKey(entry.Key)))
        {
            result.Add(new DiffEntry(key, $"{item.HttpMethod} {item.Route}", DiffKind.Removed, "사라진 엔드포인트다.")
            {
                File = item.RegistrationFile,
                Line = item.RegistrationLine
            });
        }

        return Order(result);
    }

    private static (IReadOnlyList<DiffEntry> Methods, IReadOnlyList<DiffEntry> RenameCandidates) CompareMethods(
        GraphDocument baseline,
        GraphDocument current,
        List<string> limitations)
    {
        var baselineMap = ToKeyedNodes(baseline);
        var currentMap = ToKeyedNodes(current);
        var result = new List<DiffEntry>();
        var removed = new List<GraphNode>();
        var added = new List<GraphNode>();

        foreach (var (key, node) in currentMap)
        {
            if (!baselineMap.TryGetValue(key, out var before))
            {
                added.Add(node);
                result.Add(new DiffEntry(key, node.DisplayName, DiffKind.Added, "새 메서드다.")
                {
                    File = node.File,
                    Line = node.StartLine
                });
                continue;
            }

            var changes = new List<string>();
            if (HasFingerprints(before, node))
            {
                if (!string.Equals(before.SignatureFingerprint, node.SignatureFingerprint, StringComparison.Ordinal))
                {
                    changes.Add("시그니처가 바뀌었다");
                }

                if (!string.Equals(before.BodyFingerprint, node.BodyFingerprint, StringComparison.Ordinal))
                {
                    changes.Add("본문이 바뀌었다");
                }
            }
            else if (!string.Equals(before.ReturnType, node.ReturnType, StringComparison.Ordinal))
            {
                changes.Add("반환 타입이 바뀌었다");
            }

            if (changes.Count > 0)
            {
                result.Add(new DiffEntry(key, node.DisplayName, DiffKind.Changed, string.Join(", ", changes) + ".")
                {
                    File = node.File,
                    Line = node.StartLine
                });
            }
        }

        foreach (var (key, node) in baselineMap.Where(entry => !currentMap.ContainsKey(entry.Key)))
        {
            removed.Add(node);
            result.Add(new DiffEntry(key, node.DisplayName, DiffKind.Removed, "사라진 메서드다.")
            {
                File = node.File,
                Line = node.StartLine
            });
        }

        var renameCandidates = FindRenameCandidates(removed, added);
        if (renameCandidates.Count > 0)
        {
            limitations.Add("이름 변경 후보는 본문 지문이 같은 쌍일 뿐이며 자동으로 확정하지 않는다.");
        }

        if (current.Nodes.Any(node => node.Callable == CallableKind.Lambda))
        {
            limitations.Add("람다 식별은 등록 위치·구조에 의존한다. 코드를 옮기거나 순서를 바꾸면 같은 람다인지 확인할 수 없어 비교가 불확실하다.");
        }

        return (Order(result), renameCandidates);
    }

    private static IReadOnlyList<DiffEntry> FindRenameCandidates(IReadOnlyList<GraphNode> removed, IReadOnlyList<GraphNode> added)
    {
        var result = new List<DiffEntry>();
        foreach (var before in removed.Where(node => !string.IsNullOrEmpty(node.BodyFingerprint)))
        {
            var match = added.FirstOrDefault(node =>
                string.Equals(node.BodyFingerprint, before.BodyFingerprint, StringComparison.Ordinal));
            if (match is not null)
            {
                result.Add(new DiffEntry(
                    match.StableKey.Length > 0 ? match.StableKey : match.Id,
                    $"{before.DisplayName} → {match.DisplayName}",
                    DiffKind.Changed,
                    "본문 지문이 같아 이름 변경일 수 있다(확정 아님).")
                {
                    File = match.File,
                    Line = match.StartLine
                });
            }
        }

        return result;
    }

    private static IReadOnlyList<DiffEntry> CompareEdges(GraphDocument baseline, GraphDocument current)
    {
        var baselineEdges = baseline.Edges.Select(edge => edge.From + " → " + edge.To).ToHashSet(StringComparer.Ordinal);
        var currentEdges = current.Edges.Select(edge => edge.From + " → " + edge.To).ToHashSet(StringComparer.Ordinal);
        var result = new List<DiffEntry>();

        foreach (var edge in currentEdges.Except(baselineEdges, StringComparer.Ordinal))
        {
            result.Add(new DiffEntry(edge, edge, DiffKind.Added, "새 호출이다."));
        }

        foreach (var edge in baselineEdges.Except(currentEdges, StringComparer.Ordinal))
        {
            result.Add(new DiffEntry(edge, edge, DiffKind.Removed, "사라진 호출이다."));
        }

        // 같은 엣지라도 해석 근거·확실도가 바뀌면 알려야 한다.
        var baselineSites = baseline.CallSites
            .Where(site => site.To.Length > 0)
            .GroupBy(site => site.From + " → " + site.To, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var site in current.CallSites.Where(site => site.To.Length > 0))
        {
            var key = site.From + " → " + site.To;
            if (!baselineSites.TryGetValue(key, out var before) ||
                (before.Resolution == site.Resolution && before.Confidence == site.Confidence))
            {
                continue;
            }

            result.Add(new DiffEntry(
                key,
                key,
                DiffKind.Changed,
                $"해석 근거가 {before.Resolution}/{before.Confidence} → {site.Resolution}/{site.Confidence}로 바뀌었다.")
            {
                File = site.File,
                Line = site.Line
            });
        }

        return Order(result);
    }

    private static IReadOnlyList<DiffEntry> CompareDataAccess(GraphDocument baseline, GraphDocument current)
    {
        if (baseline.DataAccess is null || current.DataAccess is null)
        {
            return
            [
                new DiffEntry(
                    "dataAccess",
                    "DB · 캐시 접근",
                    DiffKind.NotComparable,
                    "한쪽에 DB·캐시 분석이 없어 비교하지 못한다(설정에서 껐거나 옛 schema다).")
            ];
        }

        static string Key(DataAccess access) =>
            $"{access.Storage}|{access.DataSource}|{access.Target}|{access.Operation}|{access.MethodNodeId}";

        var baselineMap = baseline.DataAccess.Accesses.GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var currentMap = current.DataAccess.Accesses.GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var result = new List<DiffEntry>();

        foreach (var (key, access) in currentMap.Where(entry => !baselineMap.ContainsKey(entry.Key)))
        {
            result.Add(new DiffEntry(key, $"{access.Operation} {access.Target}", DiffKind.Added, access.MethodDisplayName)
            {
                File = access.File,
                Line = access.Line
            });
        }

        foreach (var (key, access) in baselineMap.Where(entry => !currentMap.ContainsKey(entry.Key)))
        {
            result.Add(new DiffEntry(key, $"{access.Operation} {access.Target}", DiffKind.Removed, access.MethodDisplayName)
            {
                File = access.File,
                Line = access.Line
            });
        }

        foreach (var (key, access) in currentMap)
        {
            if (baselineMap.TryGetValue(key, out var before) && before.InTransaction != access.InTransaction)
            {
                result.Add(new DiffEntry(
                    key,
                    $"{access.Operation} {access.Target}",
                    DiffKind.Changed,
                    before.InTransaction ? "트랜잭션 밖으로 나왔다." : "트랜잭션 안으로 들어갔다.")
                {
                    File = access.File,
                    Line = access.Line
                });
            }
        }

        return Order(result);
    }

    private static IReadOnlyList<DiffEntry> CompareFindings(
        GraphDocument baseline,
        GraphDocument current,
        List<string> limitations)
    {
        var baselineFindings = AllFindings(baseline);
        var currentFindings = AllFindings(current);
        var baselineRules = EnabledRules(baseline);
        var currentRules = EnabledRules(current);

        var result = new List<DiffEntry>();
        static string Key(Finding finding) => $"{finding.RuleId}|{finding.Symbol}|{finding.Target}|{finding.File}";

        var baselineMap = baselineFindings.GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var currentMap = currentFindings.GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var (key, finding) in currentMap.Where(entry => !baselineMap.ContainsKey(entry.Key)))
        {
            result.Add(new DiffEntry(key, $"{finding.RuleId} {finding.What}", DiffKind.Added, finding.Symbol)
            {
                File = finding.File,
                Line = finding.Line
            });
        }

        foreach (var (key, finding) in baselineMap.Where(entry => !currentMap.ContainsKey(entry.Key)))
        {
            var ruleTurnedOff = baselineRules.Contains(finding.RuleId) && !currentRules.Contains(finding.RuleId);
            result.Add(new DiffEntry(
                key,
                $"{finding.RuleId} {finding.What}",
                ruleTurnedOff ? DiffKind.NotComparable : DiffKind.Removed,
                ruleTurnedOff
                    ? "규칙이 꺼져서 사라졌다. 수정된 것이 아니다."
                    : finding.Symbol)
            {
                File = finding.File,
                Line = finding.Line
            });
        }

        var disabled = baselineRules.Except(currentRules, StringComparer.Ordinal).ToArray();
        if (disabled.Length > 0)
        {
            limitations.Add($"기준에서는 켜져 있었으나 지금은 꺼진 규칙이 {disabled.Length}개 있다: {string.Join(", ", disabled.Take(5))}");
        }

        return Order(result);
    }

    private static IReadOnlyList<Finding> AllFindings(GraphDocument graph) =>
        [.. graph.DataAccess?.Findings ?? [], .. graph.Middleware?.Findings ?? []];

    private static HashSet<string> EnabledRules(GraphDocument graph) =>
        (graph.DataAccess?.Rules ?? [])
        .Concat(graph.Middleware?.Rules ?? [])
        .Where(rule => rule.Enabled)
        .Select(rule => rule.Id)
        .ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, GraphNode> ToKeyedNodes(GraphDocument graph) => graph.Nodes
        .GroupBy(node => string.IsNullOrEmpty(node.StableKey) ? node.Id : node.StableKey, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static bool HasFingerprints(GraphNode left, GraphNode right) =>
        !string.IsNullOrEmpty(left.SignatureFingerprint) && !string.IsNullOrEmpty(right.SignatureFingerprint);

    private static string Display(string value) => string.IsNullOrEmpty(value) ? "(없음)" : value;

    private static IReadOnlyList<DiffEntry> Order(IEnumerable<DiffEntry> entries) => entries
        .OrderBy(item => item.Kind)
        .ThenBy(item => item.Label, StringComparer.Ordinal)
        .ToArray();
}

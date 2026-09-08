using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

/// <summary>문맥을 만들지 못한 이유. Server와 MCP가 같은 code를 돌려준다.</summary>
public sealed class ContextException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// 선택 항목·작업 목적·길이 제한을 받아 <b>결정적인 규칙</b>으로 문맥을 만든다. 이 단계에는 LLM 호출이 없다.
/// 담는 순서는 (1) 목적·질문·식별자, (2) 분석 시각·신선도·범위·미해석 사유, (3) 시그니처와 검증된 소스,
/// (4) 진입점→대상 대표 경로와 근거, (5) 인접 호출·관련 점검, (6) 테스트 후보, (7) 생략한 항목이다.
/// </summary>
public sealed class ContextBuilder(GraphFreshnessEvaluator freshnessEvaluator)
{
    private sealed record BudgetLimits(int MaxCharacters, int MaxNodes, int MaxSourceLines, int MaxFindings);

    private static BudgetLimits Limits(ContextBudget budget) => budget switch
    {
        ContextBudget.Short => new BudgetLimits(4_000, 8, 60, 3),
        ContextBudget.Detailed => new BudgetLimits(40_000, 60, 800, 20),
        _ => new BudgetLimits(14_000, 24, 260, 8)
    };

    public async Task<ContextDocument> BuildAsync(
        GraphIndex index,
        SourceReader sourceReader,
        ContextRequest request,
        CancellationToken cancellationToken = default)
    {
        var graph = index.Graph;
        if (!string.IsNullOrEmpty(request.ExpectedSnapshotId) &&
            !string.Equals(request.ExpectedSnapshotId, graph.SnapshotId, StringComparison.Ordinal))
        {
            throw new ContextException(
                "SNAPSHOT_MISMATCH",
                $"기대한 스냅샷({request.ExpectedSnapshotId})과 현재 분석({graph.SnapshotId})이 다르다. 다시 조회해야 한다.");
        }

        var limits = Limits(request.Budget);
        var freshnessReport = await freshnessEvaluator.EvaluateAsync(graph, cancellationToken);
        var freshness = new ContextFreshness(
            freshnessReport.Status.ToString().ToLowerInvariant(),
            freshnessReport.Reasons)
        {
            ChangedFiles = freshnessReport.ChangedFiles.Take(20).ToArray()
        };

        var resolved = ResolveSelection(index, request.Selection);
        var limitations = new List<string>();
        var omittedSections = new List<string>();
        var omittedRanges = new List<string>();

        // 오래된 위치 정보와 현재 소스를 섞은 "수정용 문맥"을 기본으로 만들지 않는다.
        var includeSource = request.IncludeSource &&
                            request.Intent != ContextIntent.Share &&
                            freshnessReport.Status != FreshnessStatus.Stale;
        if (request.IncludeSource && !includeSource)
        {
            limitations.Add(request.Intent == ContextIntent.Share
                ? "공유용 문맥이라 소스 본문과 절대 경로를 넣지 않았다."
                : "분석 결과가 현재 소스와 달라(stale) 소스 본문을 넣지 않았다. 다시 분석하거나 원본을 직접 확인해야 한다.");
            omittedSections.Add("sourceSnippets");
        }

        var evidence = new List<ContextEvidence>();
        var snippets = new List<ContextSourceSnippet>();
        var paths = new List<ContextPath>();
        var callers = new List<ContextNeighbor>();
        var callees = new List<ContextNeighbor>();
        var findings = new List<ContextFinding>();
        var tests = new List<ContextNeighbor>();
        var omittedNodes = 0;

        foreach (var target in resolved.Nodes.Take(limits.MaxNodes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declarationId = $"decl:{evidence.Count + 1}";
            evidence.Add(new ContextEvidence(
                declarationId,
                "declaration",
                target.File,
                target.StartLine,
                target.EndLine,
                "declared",
                nameof(CallConfidence.Certain),
                $"{target.DisplayName} · {DescribeSignature(target)}"));

            if (includeSource && !IsExcluded(target.File, request.ExcludeFiles))
            {
                var snippet = await ReadSnippetAsync(
                    sourceReader, declarationId, target.File, target.StartLine, target.EndLine, limits.MaxSourceLines, cancellationToken);
                if (snippet is not null)
                {
                    snippets.Add(snippet);
                    if (snippet.Truncated)
                    {
                        omittedRanges.Add($"{target.File}:{snippet.EndLine + 1}-{target.EndLine}");
                    }
                }
            }

            foreach (var boundary in index.GetBoundaries(target.Id).Take(5))
            {
                evidence.Add(new ContextEvidence(
                    $"bnd:{evidence.Count + 1}",
                    "boundary",
                    boundary.File,
                    boundary.Line,
                    boundary.Line,
                    boundary.Resolution.ToString(),
                    boundary.Confidence.ToString(),
                    string.IsNullOrEmpty(boundary.Note) ? boundary.DeclaredTarget : $"{boundary.DeclaredTarget} — {boundary.Note}"));
            }

            foreach (var path in BuildPaths(index, target.Id, limits.MaxNodes))
            {
                if (paths.Any(item => item.EndpointId == path.EndpointId))
                {
                    continue;
                }

                paths.Add(path);
                foreach (var step in path.Steps.Where(step => step.Resolution != "declared"))
                {
                    evidence.Add(new ContextEvidence(
                        $"call:{evidence.Count + 1}",
                        "callSite",
                        step.File,
                        step.StartLine,
                        step.StartLine,
                        step.Resolution,
                        step.Confidence,
                        step.DisplayName));
                }
            }

            AppendNeighbors(index, target.Id, callers, callees, limits.MaxNodes);
            AppendFindings(graph, target.Id, findings, limits.MaxFindings);
            AppendTestCandidates(index, target.Id, tests);
        }

        omittedNodes = Math.Max(0, resolved.Nodes.Count - limits.MaxNodes);
        if (omittedNodes > 0)
        {
            limitations.Add($"선택 범위의 메서드 {omittedNodes}개는 길이 제한으로 생략했다. 필요한 노드는 get_node/get_context로 따로 조회하면 된다.");
        }

        findings.AddRange(resolved.Findings.Where(item => findings.All(existing => existing.Id != item.Id)));
        limitations.AddRange(resolved.Limitations);
        limitations.AddRange(BuildAnalysisLimitations(graph));

        var analysis = BuildAnalysis(graph);
        var document = new ContextDocument(
            ContextDocument.Version,
            graph.SnapshotId,
            graph.InputFingerprint,
            request.Selection with { DisplayName = resolved.DisplayName },
            request.Intent.ToString(),
            request.Budget.ToString(),
            freshness,
            resolved.Summary,
            analysis,
            evidence,
            snippets,
            paths,
            Distinct(callers).Take(limits.MaxNodes).ToArray(),
            Distinct(callees).Take(limits.MaxNodes).ToArray(),
            findings.Take(limits.MaxFindings).ToArray(),
            Distinct(tests).Take(10).ToArray(),
            limitations.Distinct(StringComparer.Ordinal).ToArray(),
            false,
            new ContextOmission(omittedNodes, omittedRanges, omittedSections))
        {
            Question = request.Question
        };

        return ApplyBudget(document, limits);
    }

    /// <summary>
    /// 문맥 헤더와 누락 사유를 먼저 예약하고, 넘치는 소스부터 구간 단위로 줄인다.
    /// 상한을 넘겼는데도 줄일 소스가 없으면 인접 관계·점검을 순서대로 뺀다.
    /// </summary>
    private static ContextDocument ApplyBudget(ContextDocument document, BudgetLimits limits)
    {
        var current = document with { Markdown = ContextMarkdown.Render(document) };
        if (current.Markdown.Length <= limits.MaxCharacters)
        {
            return current;
        }

        var snippets = current.SourceSnippets.ToList();
        var omittedRanges = current.Omitted.SourceRanges.ToList();
        var omittedSections = current.Omitted.Sections.ToList();

        while (current.Markdown.Length > limits.MaxCharacters && snippets.Count > 0)
        {
            var last = snippets[^1];
            var lines = last.Content.Split('\n');
            if (lines.Length > 12)
            {
                var keep = Math.Max(6, lines.Length / 2);
                snippets[^1] = last with
                {
                    Content = string.Join('\n', lines.Take(keep)),
                    EndLine = last.StartLine + keep - 1,
                    Truncated = true
                };
                omittedRanges.Add($"{last.File}:{last.StartLine + keep}-{last.EndLine}");
            }
            else
            {
                snippets.RemoveAt(snippets.Count - 1);
                omittedRanges.Add($"{last.File}:{last.StartLine}-{last.EndLine}");
            }

            current = current with
            {
                SourceSnippets = snippets.ToArray(),
                Truncated = true,
                Omitted = current.Omitted with { SourceRanges = omittedRanges.ToArray() }
            };
            current = current with { Markdown = ContextMarkdown.Render(current) };
        }

        if (current.Markdown.Length > limits.MaxCharacters && current.Callees.Count > 0)
        {
            omittedSections.Add("callees");
            current = current with
            {
                Callees = [],
                Truncated = true,
                Omitted = current.Omitted with { Sections = omittedSections.ToArray() }
            };
            current = current with { Markdown = ContextMarkdown.Render(current) };
        }

        if (current.Markdown.Length > limits.MaxCharacters && current.Callers.Count > 0)
        {
            omittedSections.Add("callers");
            current = current with
            {
                Callers = [],
                Truncated = true,
                Omitted = current.Omitted with { Sections = omittedSections.ToArray() }
            };
            current = current with { Markdown = ContextMarkdown.Render(current) };
        }

        return current;
    }

    private static async Task<ContextSourceSnippet?> ReadSnippetAsync(
        SourceReader reader,
        string evidenceId,
        string file,
        int startLine,
        int endLine,
        int maxLines,
        CancellationToken cancellationToken)
    {
        var cappedEnd = Math.Min(endLine, startLine + maxLines - 1);
        var result = await reader.ReadAsync(file, startLine, cappedEnd, cancellationToken);
        if (result.Slice is null)
        {
            return null;
        }

        var slice = result.Slice;
        return new ContextSourceSnippet(
            evidenceId,
            slice.File,
            slice.StartLine,
            slice.EndLine,
            slice.Content,
            slice.Truncated || cappedEnd < endLine,
            slice.SourceVersion);
    }

    /// <summary>진입점에서 대상까지의 대표 경로. 진입점마다 최단 경로 하나씩만 만든다.</summary>
    private static IEnumerable<ContextPath> BuildPaths(GraphIndex index, string targetNodeId, int maxNodes)
    {
        var cameFrom = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { targetNodeId };
        var queue = new Queue<string>();
        queue.Enqueue(targetNodeId);
        var results = new List<ContextPath>();

        while (queue.Count > 0 && seen.Count <= maxNodes * 20)
        {
            var current = queue.Dequeue();
            foreach (var endpoint in index.GetEndpointsForNode(current))
            {
                var steps = new List<ContextPathStep>();
                var cursor = current;
                var guard = 0;
                while (guard++ < 100)
                {
                    var node = index.FindNode(cursor);
                    if (node is null)
                    {
                        break;
                    }

                    var next = cameFrom.TryGetValue(cursor, out var value) ? value : null;
                    var site = next is null ? null : index.GetCallSites(cursor, next).FirstOrDefault();
                    steps.Add(new ContextPathStep(
                        node.Id,
                        node.DisplayName,
                        node.File,
                        node.StartLine,
                        site?.Resolution.ToString() ?? "declared",
                        site?.Confidence.ToString() ?? nameof(CallConfidence.Certain)));
                    if (next is null || cursor == targetNodeId)
                    {
                        break;
                    }

                    cursor = next;
                }

                results.Add(new ContextPath(
                    GraphIndex.EndpointKey(endpoint),
                    endpoint.HttpMethod,
                    endpoint.Route,
                    steps,
                    guard >= 100));
                if (results.Count >= 5)
                {
                    return results;
                }
            }

            foreach (var caller in index.GetCallers(current))
            {
                if (!seen.Add(caller))
                {
                    continue;
                }

                cameFrom[caller] = current;
                queue.Enqueue(caller);
            }
        }

        return results;
    }

    private static void AppendNeighbors(
        GraphIndex index,
        string nodeId,
        List<ContextNeighbor> callers,
        List<ContextNeighbor> callees,
        int maxNodes)
    {
        foreach (var caller in index.GetCallers(nodeId).Take(maxNodes))
        {
            var node = index.FindNode(caller);
            if (node is not null)
            {
                callers.Add(new ContextNeighbor(node.Id, node.DisplayName, node.File, node.StartLine, node.Kind.ToString()));
            }
        }

        foreach (var callee in index.GetCallees(nodeId).Take(maxNodes))
        {
            var node = index.FindNode(callee);
            if (node is not null)
            {
                callees.Add(new ContextNeighbor(node.Id, node.DisplayName, node.File, node.StartLine, node.Kind.ToString()));
            }
        }
    }

    private static void AppendFindings(GraphDocument graph, string nodeId, List<ContextFinding> findings, int max)
    {
        foreach (var finding in AllFindings(graph).Where(item => item.NodeId == nodeId).Take(max))
        {
            if (findings.All(existing => existing.Id != finding.Id))
            {
                findings.Add(ToContextFinding(finding));
            }
        }
    }

    private static void AppendTestCandidates(GraphIndex index, string nodeId, List<ContextNeighbor> tests)
    {
        foreach (var caller in index.GetCallers(nodeId))
        {
            var node = index.FindNode(caller);
            if (node is null)
            {
                continue;
            }

            if (node.Namespace.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
                node.ClassId.Contains("Test", StringComparison.OrdinalIgnoreCase))
            {
                tests.Add(new ContextNeighbor(node.Id, node.DisplayName, node.File, node.StartLine, "reached"));
            }
        }
    }

    private static ResolvedSelection ResolveSelection(GraphIndex index, ContextSelection selection)
    {
        var graph = index.Graph;
        switch (selection.Kind)
        {
            case SelectionKind.Node:
            {
                var node = index.FindNode(selection.Id) ?? index.FindNodeByStableKey(selection.Id);
                if (node is null)
                {
                    throw new ContextException("SELECTION_NOT_FOUND", $"노드를 찾을 수 없다: {selection.Id}");
                }

                return new ResolvedSelection(
                    [node],
                    node.DisplayName,
                    $"{node.DisplayName}의 문맥이다. {node.File}:{node.StartLine}-{node.EndLine}에 선언돼 있다.",
                    [],
                    []);
            }

            case SelectionKind.Endpoint:
            {
                var endpoint = index.FindEndpoint(selection.Id)
                    ?? throw new ContextException("SELECTION_NOT_FOUND", $"엔드포인트를 찾을 수 없다: {selection.Id}");
                var handler = index.FindNode(endpoint.NodeId);
                var notes = endpoint.Notes.ToList();
                if (endpoint.Confidence == CallConfidence.Estimated)
                {
                    notes.Add("이 엔드포인트의 라우트나 핸들러 일부는 추정이다.");
                }

                return new ResolvedSelection(
                    handler is null ? [] : [handler],
                    $"{endpoint.HttpMethod} {endpoint.Route}",
                    $"{endpoint.HttpMethod} {endpoint.Route} 진입점의 문맥이다. 등록 위치는 {endpoint.RegistrationFile}:{endpoint.RegistrationLine}다.",
                    [],
                    notes);
            }

            case SelectionKind.Class:
            {
                var graphClass = index.FindClass(selection.Id)
                    ?? throw new ContextException("SELECTION_NOT_FOUND", $"클래스를 찾을 수 없다: {selection.Id}");
                var nodes = graphClass.MethodIds
                    .Select(index.FindNode)
                    .Where(node => node is not null)
                    .Select(node => node!)
                    .OrderBy(node => node.StartLine)
                    .ToArray();
                var relations = new List<string>();
                if (graphClass.BaseTypeName is not null)
                {
                    relations.Add($"상위 타입 {graphClass.BaseTypeName}");
                }

                if (graphClass.InterfaceNames.Count > 0)
                {
                    relations.Add($"구현 {string.Join(", ", graphClass.InterfaceNames)}");
                }

                return new ResolvedSelection(
                    nodes,
                    graphClass.Name,
                    $"{graphClass.Id} 클래스의 문맥이다. {graphClass.File}:{graphClass.StartLine}-{graphClass.EndLine}, 멤버 {graphClass.Members.Count}개." +
                    (relations.Count > 0 ? " " + string.Join(" · ", relations) + "." : string.Empty),
                    [],
                    []);
            }

            case SelectionKind.Namespace:
            {
                var classes = graph.Classes.Where(item => item.Namespace == selection.Id).ToArray();
                if (classes.Length == 0)
                {
                    throw new ContextException("SELECTION_NOT_FOUND", $"네임스페이스를 찾을 수 없다: {selection.Id}");
                }

                var nodes = classes
                    .SelectMany(item => item.MethodIds)
                    .Select(index.FindNode)
                    .Where(node => node is not null)
                    .Select(node => node!)
                    .ToArray();
                return new ResolvedSelection(
                    nodes,
                    selection.Id,
                    $"네임스페이스 {selection.Id}의 문맥이다. 클래스 {classes.Length}개, 메서드 {nodes.Length}개.",
                    [],
                    ["네임스페이스 선택은 대표 멤버만 담는다. 특정 메서드를 보려면 그 노드를 골라야 한다."]);
            }

            case SelectionKind.Finding:
            {
                var finding = AllFindings(graph).FirstOrDefault(item => item.Id == selection.Id)
                    ?? throw new ContextException("SELECTION_NOT_FOUND", $"점검 항목을 찾을 수 없다: {selection.Id}");
                var node = finding.NodeId is null ? null : index.FindNode(finding.NodeId);
                return new ResolvedSelection(
                    node is null ? [] : [node],
                    $"{finding.RuleId} {finding.What}",
                    $"점검 항목 {finding.RuleId}({finding.Severity}/{finding.Confidence})의 문맥이다. {finding.Symbol} · {finding.File}:{finding.Line}.",
                    [ToContextFinding(finding)],
                    finding.Confidence == FindingConfidence.Estimated
                        ? ["이 점검은 추정 판정이다. 실제 코드로 확인해야 한다."]
                        : []);
            }

            case SelectionKind.WritePath:
            {
                var path = graph.DataAccess?.WritePaths.FirstOrDefault(item => item.Id == selection.Id)
                    ?? throw new ContextException("SELECTION_NOT_FOUND", $"쓰기 경로를 찾을 수 없다: {selection.Id}");
                var node = index.FindNode(path.MethodNodeId);
                var relatedFindings = AllFindings(graph)
                    .Where(item => path.FindingIds.Contains(item.Id))
                    .Select(ToContextFinding)
                    .ToArray();
                return new ResolvedSelection(
                    node is null ? [] : [node],
                    path.MethodDisplayName,
                    $"쓰기 경로 {path.MethodDisplayName}의 문맥이다. 단계 {path.Steps.Count}개" +
                    (path.InTransaction ? ", 트랜잭션 안에서 실행된다." : ", 트랜잭션 없이 실행된다."),
                    relatedFindings,
                    [$"쓰기 단계: {string.Join(" → ", path.Steps.Select(step => $"{step.Label}({step.Compensation})"))}"]);
            }

            case SelectionKind.MiddlewareStep:
            {
                var step = graph.Middleware?.Steps.FirstOrDefault(item => item.Id == selection.Id)
                    ?? throw new ContextException("SELECTION_NOT_FOUND", $"미들웨어 단계를 찾을 수 없다: {selection.Id}");
                var node = step.ClassId is null
                    ? null
                    : graph.Nodes.FirstOrDefault(item => item.ClassId == step.ClassId && item.DisplayName.EndsWith("InvokeAsync", StringComparison.Ordinal));
                return new ResolvedSelection(
                    node is null ? [] : [node],
                    step.Name,
                    $"미들웨어 {step.Order}단계 {step.Name}의 문맥이다. 등록 {step.RegistrationFile}:{step.RegistrationLine}.",
                    (graph.Middleware?.Findings ?? []).Where(item => item.StepId == step.Id).Select(ToContextFinding).ToArray(),
                    step.ShortCircuit is null ? [] : [$"단락 지점: {step.ShortCircuit.StatusCode} ({step.ShortCircuit.Condition})"]);
            }

            case SelectionKind.StartupStep:
            {
                var step = FindStartupStep(graph.StartupFlow, selection.Id)
                    ?? throw new ContextException("SELECTION_NOT_FOUND", $"시작 흐름 단계를 찾을 수 없다: {selection.Id}");
                return new ResolvedSelection(
                    [],
                    step.Label,
                    $"시작 흐름 단계 {step.Label}의 문맥이다. {step.File}:{step.Line}.",
                    [],
                    ["시작 흐름 단계는 호출 그래프의 노드가 아니라 Program.cs를 소스 순서대로 읽은 결과다."]);
            }

            default:
                throw new ContextException("SELECTION_KIND", $"지원하지 않는 선택 종류다: {selection.Kind}");
        }
    }

    private static StartupStep? FindStartupStep(StartupFlowDocument? flow, string key)
    {
        if (flow is null)
        {
            return null;
        }

        return Search(flow.ServiceSteps) ?? Search(flow.MiddlewareSteps);

        StartupStep? Search(IEnumerable<StartupStep> steps)
        {
            foreach (var step in steps)
            {
                if ($"{step.File}:{step.Line}:{step.Label}" == key || step.Label == key)
                {
                    return step;
                }

                var child = Search(step.Children);
                if (child is not null)
                {
                    return child;
                }
            }

            return null;
        }
    }

    private static ContextAnalysis BuildAnalysis(GraphDocument graph)
    {
        var diagnostics = graph.Diagnostics;
        return new ContextAnalysis(
            graph.AnalyzedAt,
            diagnostics?.AnalysisScope ?? "unknown",
            diagnostics?.LoadedProjects ?? 0,
            diagnostics?.TotalProjects ?? 0,
            diagnostics?.ProjectsWithCompileErrors ?? 0,
            diagnostics?.Calls.Inspected ?? 0,
            diagnostics?.Calls.Resolved ?? 0,
            diagnostics?.Calls.ResolutionRate ?? 0);
    }

    private static IReadOnlyList<string> BuildAnalysisLimitations(GraphDocument graph)
    {
        var limitations = new List<string>
        {
            "이 문맥은 정적 분석 결과다. 실제 요청의 실행 이력이나 시간은 담고 있지 않다."
        };

        var diagnostics = graph.Diagnostics;
        if (diagnostics is null)
        {
            limitations.Add("이 그래프에는 분석 진단이 없다(schema v6 이하). 무엇을 못 봤는지 확인하려면 다시 분석해야 한다.");
            return limitations;
        }

        if (diagnostics.ProjectsWithCompileErrors > 0)
        {
            limitations.Add($"컴파일 오류가 있는 프로젝트가 {diagnostics.ProjectsWithCompileErrors}개다. 그만큼 호출 관계가 빠졌을 수 있다.");
        }

        if (diagnostics.Calls.Ambiguous > 0 || diagnostics.Calls.Unresolved > 0)
        {
            limitations.Add(
                $"확정하지 못한 호출 지점이 {diagnostics.Calls.Ambiguous + diagnostics.Calls.Unresolved}곳이다(모호 {diagnostics.Calls.Ambiguous} · 미해석 {diagnostics.Calls.Unresolved}). 그래프의 빈 부분을 \"호출 없음\"으로 보면 안 된다.");
        }

        if (diagnostics.Calls.SingleImplementation > 0)
        {
            limitations.Add($"DI 등록 없이 유일 구현체로 이은 호출이 {diagnostics.Calls.SingleImplementation}곳 있다(추정).");
        }

        limitations.AddRange(diagnostics.Limitations);
        limitations.AddRange(diagnostics.EntryPointNotes);
        if (diagnostics.ChangedDuringAnalysis)
        {
            limitations.Add("분석 도중에 입력 파일이 바뀌었다. 결과가 한 시점의 코드와 정확히 대응하지 않을 수 있다.");
        }

        return limitations;
    }

    private static IReadOnlyList<Finding> AllFindings(GraphDocument graph) =>
        [.. graph.DataAccess?.Findings ?? [], .. graph.Middleware?.Findings ?? []];

    private static ContextFinding ToContextFinding(Finding finding) => new(
        finding.Id,
        finding.RuleId,
        finding.Severity.ToString(),
        finding.Confidence.ToString(),
        finding.What,
        finding.Why,
        finding.Fix,
        finding.Symbol,
        finding.File,
        finding.Line);

    private static string DescribeSignature(GraphNode node) =>
        $"{node.Accessibility} {node.ReturnType} {node.DisplayName}({string.Join(", ", node.ParameterTypes)})".Trim();

    private static bool IsExcluded(string file, IReadOnlyList<string> excluded) =>
        excluded.Any(prefix => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<ContextNeighbor> Distinct(IEnumerable<ContextNeighbor> items) =>
        items.DistinctBy(item => item.NodeId, StringComparer.Ordinal);

    private sealed record ResolvedSelection(
        IReadOnlyList<GraphNode> Nodes,
        string DisplayName,
        string Summary,
        IReadOnlyList<ContextFinding> Findings,
        IReadOnlyList<string> Limitations);
}

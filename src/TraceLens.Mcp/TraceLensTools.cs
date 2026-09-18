using System.ComponentModel;
using ModelContextProtocol.Server;
using TraceLens.Analyzer.Models;
using TraceLens.Context;

namespace TraceLens.Mcp;

/// <summary>
/// TraceLens 그래프를 읽는 로컬 MCP 도구다.
/// 모든 도구는 <b>이미 저장된 분석 결과</b>만 읽고 재분석을 실행하지 않는다.
/// 응답에는 항상 <c>snapshotId</c>가 들어가고, 후속 호출에 <c>expectedSnapshotId</c>를 넘기면
/// 그 사이에 분석이 바뀌었는지 알 수 있다.
/// </summary>
[McpServerToolType]
public sealed class TraceLensTools(GraphFileStore store)
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    [McpServerTool(Name = "get_analysis_status")]
    [Description(
        "지금 읽고 있는 분석의 대상·시각·신선도·지원 기능·진단 요약을 조회한다. " +
        "다른 도구를 부르기 전에 이 값을 먼저 보면 결과가 얼마나 믿을 만한지 알 수 있다. 재분석은 하지 않는다. " +
        "무엇을 묻든 이 도구를 가장 먼저 부른다. 응답의 suggestedNextTools가 다음에 부를 도구를 알려 준다. " +
        "Call this first, whatever the question; suggestedNextTools tells you where to go next.")]
    public async Task<AnalysisStatusResult> GetAnalysisStatusAsync(
        [Description("기대하는 분석 스냅샷 ID. 다르면 오류로 알려준다.")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var graph = session.Index.Graph;
        var freshness = await new GraphFreshnessEvaluator().EvaluateAsync(graph, cancellationToken);
        var issues = await store.GetIssuesAsync(cancellationToken);
        return new AnalysisStatusResult(
            graph.SnapshotId,
            graph.InputFingerprint,
            graph.SolutionPath,
            graph.AnalyzedAt,
            graph.SchemaVersion,
            graph.AnalyzerVersion,
            freshness.Status.ToString().ToLowerInvariant(),
            freshness.ChangedFiles.Take(30).ToArray(),
            freshness.Reasons,
            graph.EntryPoints.Count,
            graph.Nodes.Count,
            graph.Edges.Count,
            graph.CallSites.Count > 0,
            graph.Diagnostics,
            issues.Select(issue => $"{issue.Code}: {issue.Message}").ToArray())
        {
            DataAccessNotes = graph.DataAccess?.Notes ?? [],
            SuggestedNextTools = SuggestNextTools(graph, freshness.Status.ToString())
        };
    }

    /// <summary>
    /// 신선도·진입점 수·점검 수를 보고 다음에 부를 만한 도구를 고른다.
    /// 아무것도 없을 때(진입점 0)는 도구를 권하지 않고 사용자에게 재분석을 요청하는 편이 맞다.
    /// </summary>
    private static IReadOnlyList<string> SuggestNextTools(GraphDocument graph, string freshness)
    {
        var tools = new List<string>();
        if (graph.EntryPoints.Count > 0)
        {
            tools.Add("get_entrypoints");
        }

        if (graph.Nodes.Count > 0)
        {
            tools.Add("search_symbol");
        }

        var findingCount = (graph.DataAccess?.Findings.Count ?? 0) + (graph.Middleware?.Findings.Count ?? 0);
        if (findingCount > 0)
        {
            tools.Add("get_findings");
        }

        // stale이면 무엇을 부르든 답에 그 사실을 적어야 한다. 도구 하나를 더 권하는 것보다 그게 중요하다.
        if (string.Equals(freshness, "stale", StringComparison.OrdinalIgnoreCase))
        {
            tools.Insert(0, "get_analysis_status");
        }

        return tools;
    }

    [McpServerTool(Name = "get_context")]
    [Description(
        "선택한 노드·엔드포인트·클래스·점검 항목에 대해 TraceLens UI와 같은 작업 문맥을 만든다. " +
        "시그니처·검증된 소스·진입점까지의 경로·근거·한계를 한 번에 돌려준다. selectionKind는 " +
        "node, endpoint, class, namespace, finding, writePath, middlewareStep, startupStep 중 하나다. " +
        "설명·변경 계획·검토처럼 답을 쓰기 위한 문맥이 필요할 때 쓴다. 구조만 훑으려면 get_entrypoint_flow가 가볍다. " +
        "Use it when you need context to write an answer; use get_entrypoint_flow to just skim the structure.")]
    public async Task<ContextDocument> GetContextAsync(
        [Description("선택 종류: node|endpoint|class|namespace|finding|writePath|middlewareStep|startupStep")] string selectionKind,
        [Description("선택 대상의 ID")] string selectionId,
        [Description("작업 목적: explain|planChange|reviewChange|investigateData|share")] string? intent,
        [Description("길이 예산: short|balanced|detailed")] string? budget,
        [Description("사용자 질문(있으면 문맥 맨 앞에 그대로 들어간다)")] string? question,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        if (!Enum.TryParse<SelectionKind>(selectionKind, ignoreCase: true, out var kind))
        {
            throw new ArgumentException(
                $"지원하지 않는 선택 종류다: {selectionKind}. 쓸 수 있는 값: {string.Join(", ", Enum.GetNames<SelectionKind>())}",
                nameof(selectionKind));
        }

        return await new ContextBuilder(new GraphFreshnessEvaluator()).BuildAsync(
            session.Index,
            session.Source,
            new ContextRequest(
                new ContextSelection(kind, selectionId),
                Parse(intent, ContextIntent.Explain),
                Parse(budget, ContextBudget.Balanced))
            {
                Question = question ?? string.Empty,
                ExpectedSnapshotId = expectedSnapshotId
            },
            cancellationToken);
    }

    [McpServerTool(Name = "get_entrypoint_flow")]
    [Description(
        "HTTP 진입점 하나에서 시작하는 제한된 호출 경로와 그 경계를 조회한다. " +
        "전체 그래프를 받지 않고 이 진입점에 필요한 만큼만 본다. " +
        "한 진입점의 구조를 볼 때 쓴다. 소스와 근거까지 필요하면 get_context로 넘어간다. " +
        "Use it for the shape of one entry point; switch to get_context when you need source and evidence.")]
    public async Task<EntryPointFlowResult> GetEntryPointFlowAsync(
        [Description("엔드포인트 ID(또는 \"GET /orders\" 형태)")] string endpointId,
        [Description("따라갈 최대 깊이(기본 4)")] int? depth,
        [Description("돌려줄 최대 노드 수(기본 80)")] int? maxNodes,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var index = session.Index;
        var endpoint = index.FindEndpoint(endpointId)
            ?? throw new KeyNotFoundException($"진입점을 찾을 수 없다: {endpointId}");

        var maxDepth = Math.Clamp(depth ?? 4, 1, 32);
        var nodeLimit = Math.Clamp(maxNodes ?? 80, 1, 2000);
        var nodes = new List<FlowNode>();
        var edges = new List<GraphEdge>();
        var boundaries = new List<CallSite>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string NodeId, int Depth)>();
        queue.Enqueue((endpoint.NodeId, 0));
        visited.Add(endpoint.NodeId);
        var truncated = false;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (nodeId, currentDepth) = queue.Dequeue();
            var node = index.FindNode(nodeId);
            if (node is null)
            {
                continue;
            }

            nodes.Add(new FlowNode(node, currentDepth));
            boundaries.AddRange(index.GetBoundaries(nodeId).Take(3));
            if (currentDepth >= maxDepth)
            {
                truncated = true;
                continue;
            }

            foreach (var callee in index.GetCallees(nodeId))
            {
                edges.Add(new GraphEdge(nodeId, callee));
                if (!visited.Add(callee))
                {
                    continue;
                }

                if (nodes.Count + queue.Count >= nodeLimit)
                {
                    truncated = true;
                    continue;
                }

                queue.Enqueue((callee, currentDepth + 1));
            }
        }

        return new EntryPointFlowResult(
            index.SnapshotId,
            GraphIndex.EndpointKey(endpoint),
            endpoint.HttpMethod,
            endpoint.Route,
            endpoint.NodeId,
            nodes,
            edges.DistinctBy(edge => edge.From + "->" + edge.To, StringComparer.Ordinal).ToArray(),
            boundaries,
            truncated,
            endpoint.Notes);
    }

    [McpServerTool(Name = "get_findings")]
    [Description(
        "DB·캐시와 미들웨어 점검 결과를 근거·확실도와 함께 조회한다. " +
        "검사하지 않은 규칙도 카탈로그에 \"꺼짐\"으로 남아 있어 무엇을 보지 않았는지 알 수 있다. " +
        "규칙 ID 접두사(COMP-, TX-, MW-, SHARD-)로 거른다. 쓰기 경로 안전성은 COMP-로 시작한다. " +
        "Filter by rule id prefix (COMP-, TX-, MW-, SHARD-); write-path safety starts at COMP-.")]
    public async Task<FindingsResult> GetFindingsAsync(
        [Description("이 노드와 연결된 점검만 본다")] string? nodeId,
        [Description("이 규칙 ID의 점검만 본다")] string? ruleId,
        [Description("한 번에 돌려줄 최대 개수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var graph = session.Index.Graph;
        var all = ((IEnumerable<Finding>)(graph.DataAccess?.Findings ?? []))
            .Concat(graph.Middleware?.Findings ?? [])
            .Where(item => nodeId is null || item.NodeId == nodeId)
            .Where(item => ruleId is null || string.Equals(item.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var page = Page(all, limit, cursor);
        var rules = ((IEnumerable<RuleInfo>)(graph.DataAccess?.Rules ?? []))
            .Concat(graph.Middleware?.Rules ?? [])
            .ToArray();
        return new FindingsResult(
            graph.SnapshotId,
            page.Items,
            all.Length,
            page.NextCursor,
            rules.Where(rule => !rule.Enabled).Select(rule => rule.Id).ToArray(),
            rules.Length);
    }

    [McpServerTool(Name = "get_impact")]
    [Description(
        "이 메서드를 바꾸면 영향을 받을 수 있는 HTTP 진입점 후보와 그 근거 경로를 조회한다. " +
        "분석한 호출 관계에서 도출한 후보이며, reflection·외부 호출·이벤트 구독은 증명하지 않는다. " +
        "호출자가 없다고 삭제해도 안전하다는 뜻이 아니다. " +
        "nodeId를 모르면 search_symbol로 먼저 찾아 그 id를 넘긴다. " +
        "If you do not know the nodeId, find it with search_symbol first and pass that id here.")]
    public async Task<ImpactResult> GetImpactAsync(
        [Description("영향을 확인할 노드 ID 목록")] string[] nodeIds,
        [Description("역방향 탐색 최대 깊이(기본 12)")] int? maxDepth,
        [Description("방문할 최대 노드 수(기본 4000)")] int? maxNodes,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        return new ImpactAnalyzer().Analyze(
            session.Index,
            new ImpactRequest(
                nodeIds,
                Math.Clamp(maxDepth ?? 12, 1, 64),
                Math.Clamp(maxNodes ?? 4000, 1, 20000)),
            cancellationToken);
    }

    [McpServerTool(Name = "compare_snapshots")]
    [Description(
        "보관해 둔 기준 스냅샷과 현재 분석의 구조·본문·점검 차이를 조회한다. " +
        "비교 조건(대상·분석기·규칙·범위)이 다르면 그 사실을 함께 돌려준다. " +
        "\"차이 없음\"은 안전하다는 뜻도, 테스트가 통과한다는 뜻도 아니다. " +
        "수정 전후를 비교할 때만 쓴다. 기준은 사용자가 UI에서 저장해 두어야 있다. " +
        "Use it only to compare before and after a change; the baseline must have been saved by the user.")]
    public async Task<SnapshotDiff> CompareSnapshotsAsync(
        [Description("기준 스냅샷 ID(.tracelens/history 아래에 보관한 것)")] string baselineId,
        [Description("비교 대상 스냅샷 ID. 비우면 지금 그래프와 비교한다.")] string? currentId,
        [Description("기대하는 분석 스냅샷 ID. 다르면 오류로 알려준다.")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var graph = session.Index.Graph;
        var snapshots = new SnapshotStore();
        var baseline = await snapshots.LoadAsync(graph, baselineId, cancellationToken);
        if (!baseline.Ok || baseline.Graph is null)
        {
            throw new KeyNotFoundException($"기준 스냅샷을 읽을 수 없다: {baselineId}");
        }

        var target = graph;
        if (!string.IsNullOrWhiteSpace(currentId) &&
            !string.Equals(currentId, graph.SnapshotId, StringComparison.Ordinal))
        {
            var stored = await snapshots.LoadAsync(graph, currentId, cancellationToken);
            target = stored.Graph ?? throw new KeyNotFoundException($"비교 대상 스냅샷을 읽을 수 없다: {currentId}");
        }

        return new SnapshotComparer().Compare(baseline.Graph, target);
    }

    [McpServerTool(Name = "get_node")]
    [Description(
        "노드 ID로 메타데이터와 해당 메서드의 소스를 조회한다. 저장된 분석만 읽고 재분석하지 않는다. " +
        "노드 하나만 볼 때 쓴다. 답을 쓰기 위한 문맥이 필요하면 get_context가 낫다. " +
        "Use it to look at one node; prefer get_context when you need context to write an answer.")]
    public async Task<NodeResult> GetNodeAsync(
        [Description("TraceLens 그래프 노드 ID")] string nodeId,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var node = FindNode(session.Index, nodeId);
        var result = await session.Source.ReadAsync(node.File, node.StartLine, node.EndLine, cancellationToken);
        if (result.Slice is null)
        {
            // 거부는 예외 문자열이 아니라 code가 있는 구조로 돌려준다. 에이전트가 무엇 때문에 막혔는지 구분할 수 있어야 한다.
            return new NodeResult(node, null, session.SnapshotId)
            {
                Denied = ToDenial(result.Access)
            };
        }

        return new NodeResult(node, ToSourceResult(result.Slice), session.SnapshotId);
    }

    [McpServerTool(Name = "get_source")]
    [Description(
        "분석 대상 솔루션 내부 파일의 지정된 라인 범위를 읽는다. " +
        "분석 당시와 내용이 같은지(sourceVersion)를 함께 돌려준다. " +
        "다른 도구가 준 파일·줄을 눈으로 확인할 때 쓴다. 파일을 찾아다니는 용도가 아니다. " +
        "Use it to verify a file and line another tool returned, not to browse for files.")]
    public async Task<SourceResult> GetSourceAsync(
        [Description("솔루션 루트 기준 상대 파일 경로")] string file,
        [Description("1부터 시작하는 시작 라인")] int startLine,
        [Description("1부터 시작하는 끝 라인")] int endLine,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var result = await session.Source.ReadAsync(file, startLine, endLine, cancellationToken);
        if (result.Slice is null)
        {
            return new SourceResult(file, startLine, endLine, string.Empty, "unknown", false)
            {
                Denied = ToDenial(result.Access)
            };
        }

        return ToSourceResult(result.Slice);
    }

    [McpServerTool(Name = "get_callers")]
    [Description(
        "지정한 노드를 직접 호출하는 인접 노드와 그 호출 근거를 조회한다. " +
        "빈 목록은 호출자가 없다는 증명이 아니다 — 확정하지 못한 호출은 경계 목록(boundaries)에 남는다. " +
        "한 홉만 본다. 진입점까지 거슬러 올라가려면 get_impact를 쓴다. " +
        "This is one hop only; use get_impact to walk all the way back to entry points.")]
    public async Task<NeighborResult> GetCallersAsync(
        [Description("TraceLens 그래프 노드 ID")] string nodeId,
        [Description("한 번에 돌려줄 최대 개수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var index = session.Index;
        _ = FindNode(index, nodeId);
        var neighbors = index.GetCallers(nodeId)
            .Select(id => (Node: index.FindNode(id), Sites: index.GetCallSites(id, nodeId)))
            .Where(item => item.Node is not null)
            .Select(item => new Neighbor(item.Node!, item.Sites))
            .ToArray();
        var page = Page(neighbors, limit, cursor);
        return new NeighborResult(index.SnapshotId, page.Items, neighbors.Length, page.NextCursor, index.GetBoundaries(nodeId));
    }

    [McpServerTool(Name = "get_callees")]
    [Description(
        "지정한 노드가 직접 호출하는 인접 노드와 그 호출 근거를 조회한다. " +
        "빈 목록은 호출이 없다는 증명이 아니다 — 확정하지 못한 호출은 경계 목록(boundaries)에 남는다. " +
        "한 홉만 본다. 진입점 전 구간이 필요하면 get_entrypoint_flow를 쓴다. " +
        "This is one hop only; use get_entrypoint_flow for a whole entry point path.")]
    public async Task<NeighborResult> GetCalleesAsync(
        [Description("TraceLens 그래프 노드 ID")] string nodeId,
        [Description("한 번에 돌려줄 최대 개수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var index = session.Index;
        _ = FindNode(index, nodeId);
        var neighbors = index.GetCallees(nodeId)
            .Select(id => (Node: index.FindNode(id), Sites: index.GetCallSites(nodeId, id)))
            .Where(item => item.Node is not null)
            .Select(item => new Neighbor(item.Node!, item.Sites))
            .ToArray();
        var page = Page(neighbors, limit, cursor);
        return new NeighborResult(index.SnapshotId, page.Items, neighbors.Length, page.NextCursor, index.GetBoundaries(nodeId));
    }

    [McpServerTool(Name = "get_entrypoints")]
    [Description(
        "현재 TraceLens 그래프의 HTTP 진입점 목록을 조회한다. 같은 메서드에 여러 라우트가 걸리면 각각 나온다. " +
        "\"이 API가 뭘 하나\"의 출발점이다. 고른 endpointId를 get_entrypoint_flow나 get_context에 넘긴다. " +
        "Start here for \"what does this API do\"; pass the endpointId to get_entrypoint_flow or get_context.")]
    public async Task<EntryPointsResult> GetEntryPointsAsync(
        [Description("라우트나 HTTP 동사에 포함된 문자열로 거른다")] string? query,
        [Description("한 번에 돌려줄 최대 개수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var graph = session.Index.Graph;
        var filtered = graph.EntryPoints
            .Where(item => query is null ||
                           item.Route.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           item.HttpMethod.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var page = Page(filtered, limit, cursor);
        return new EntryPointsResult(
            graph.SnapshotId,
            page.Items,
            filtered.Length,
            page.NextCursor,
            graph.Diagnostics?.EntryPointNotes ?? []);
    }

    [McpServerTool(Name = "search_symbol")]
    [Description(
        "표시 이름, 전체 노드 ID 또는 네임스페이스에 포함된 문자열로 노드를 검색한다. " +
        "nodeId를 모를 때 가장 먼저 쓴다. 결과의 id를 get_node·get_impact·get_context에 그대로 넘긴다. " +
        "Use it first when you do not know a nodeId; pass the result id to get_node, get_impact or get_context.")]
    public async Task<SearchResult> SearchSymbolAsync(
        [Description("대소문자를 구분하지 않는 검색 문자열")] string query,
        [Description("한 번에 돌려줄 최대 개수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("query가 필요하다.", nameof(query));
        }

        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var matches = session.Index.Graph.Nodes
            .Where(node => node.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           node.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           node.Namespace.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var page = Page(matches, limit, cursor);
        return new SearchResult(session.SnapshotId, page.Items, matches.Length, page.NextCursor);
    }

    private static GraphNode FindNode(GraphIndex index, string nodeId) =>
        index.FindNode(nodeId) ?? index.FindNodeByStableKey(nodeId)
        ?? throw new KeyNotFoundException($"노드를 찾을 수 없다: {nodeId}");

    /// <summary>접근 거부를 예외가 아니라 code가 있는 구조로 바꾼다.</summary>
    private static SourceDenial ToDenial(SourceAccessResult access) =>
        new("SOURCE_DENIED", access.Denial.ToString(), access.Message);

    private static SourceResult ToSourceResult(SourceSlice slice) =>
        new(slice.File, slice.StartLine, slice.EndLine, slice.Content, slice.SourceVersion, slice.Truncated);

    private static (IReadOnlyList<T> Items, int? NextCursor) Page<T>(IReadOnlyList<T> items, int? limit, int? cursor)
    {
        var start = Math.Max(0, cursor ?? 0);
        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var page = items.Skip(start).Take(size).ToArray();
        var next = start + page.Length;
        return (page, next < items.Count ? next : null);
    }

    private static TEnum Parse<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}

/// <param name="Code">SOURCE_DENIED 등. 왜 막혔는지 구분할 수 있게 사유도 함께 준다.</param>
public sealed record SourceDenial(string Code, string Reason, string Message);

/// <param name="SourceVersion">snapshot이면 분석 당시와 같은 내용이고, current면 그 뒤로 바뀐 현재 소스다.</param>
public sealed record SourceResult(
    string File,
    int StartLine,
    int EndLine,
    string Content,
    string SourceVersion,
    bool Truncated)
{
    /// <summary>소스를 읽지 못했을 때의 사유. 읽었으면 null이다(예외 대신 구조로 돌려준다).</summary>
    public SourceDenial? Denied { get; init; }
}

public sealed record NodeResult(GraphNode Node, SourceResult? Source, string SnapshotId)
{
    /// <summary>소스를 읽지 못했을 때의 사유. 노드 메타데이터는 그대로 돌려준다.</summary>
    public SourceDenial? Denied { get; init; }
}

public sealed record Neighbor(GraphNode Node, IReadOnlyList<CallSite> CallSites);

/// <param name="Boundaries">확정하지 못한 호출. 목록이 비었다고 "호출 없음"으로 보면 안 된다.</param>
public sealed record NeighborResult(
    string SnapshotId,
    IReadOnlyList<Neighbor> Items,
    int TotalCount,
    int? NextCursor,
    IReadOnlyList<CallSite> Boundaries);

public sealed record SearchResult(string SnapshotId, IReadOnlyList<GraphNode> Items, int TotalCount, int? NextCursor);

public sealed record EntryPointsResult(
    string SnapshotId,
    IReadOnlyList<EntryPoint> Items,
    int TotalCount,
    int? NextCursor,
    IReadOnlyList<string> Notes);

public sealed record FlowNode(GraphNode Node, int Depth);

public sealed record EntryPointFlowResult(
    string SnapshotId,
    string EndpointId,
    string HttpMethod,
    string Route,
    string HandlerNodeId,
    IReadOnlyList<FlowNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<CallSite> Boundaries,
    bool Truncated,
    IReadOnlyList<string> Notes);

/// <param name="DisabledRuleIds">이번 분석에서 검사하지 않은 규칙. 무엇을 보지 않았는지 알리기 위해 함께 돌려준다.</param>
public sealed record FindingsResult(
    string SnapshotId,
    IReadOnlyList<Finding> Items,
    int TotalCount,
    int? NextCursor,
    IReadOnlyList<string> DisabledRuleIds,
    int RuleCount);

public sealed record AnalysisStatusResult(
    string SnapshotId,
    string InputFingerprint,
    string SolutionPath,
    DateTimeOffset AnalyzedAt,
    int SchemaVersion,
    string AnalyzerVersion,
    string Freshness,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> FreshnessReasons,
    int EntryPointCount,
    int NodeCount,
    int EdgeCount,
    bool HasCallSites,
    AnalysisDiagnostics? Diagnostics,
    IReadOnlyList<string> GraphIssues)
{
    /// <summary>
    /// DB·캐시에서 인식했지만 해석하지 않은 입력에 대한 사실(EF Core만 쓰는 프로젝트 등).
    /// 접근이 0건일 때 "DB를 안 쓴다"로 읽지 않게 한다.
    /// </summary>
    public IReadOnlyList<string> DataAccessNotes { get; init; } = [];

    /// <summary>
    /// 지금 상태에서 다음에 부를 만한 도구. 도구 설명만으로는 분기를 알기 어렵다.
    /// 보장이 아니라 제안이다 — 질문에 따라 다른 도구가 맞을 수 있다.
    /// </summary>
    public IReadOnlyList<string> SuggestedNextTools { get; init; } = [];
}

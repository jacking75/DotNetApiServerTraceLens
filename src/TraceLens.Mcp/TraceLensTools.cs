using System.ComponentModel;
using Microsoft.Extensions.Logging;
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
public sealed class TraceLensTools(GraphFileStore store, ILogger<TraceLensTools> logger)
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    /// <summary>한 번에 읽는 소스 줄 수의 기본값. 넘으면 잘랐다는 사실과 이어 읽을 줄을 함께 준다(T61).</summary>
    private const int DefaultMaxSourceLines = 400;

    /// <summary>
    /// 신선도 캐시. 세션 하나가 이것 하나를 재사용한다 —
    /// 예전에는 도구마다 평가기를 새로 만들어 호출마다 솔루션 전체를 다시 해시했다(T61).
    /// </summary>
    private readonly GraphFreshnessCache freshnessCache = new(new GraphFreshnessEvaluator());

    /// <summary>
    /// 도구 하나를 감싼다. 예외를 <c>{error:{code,message}}</c>로 바꾸고 로그에 남긴다.
    ///
    /// 예외를 그대로 던지면 에이전트가 받는 것은 문자열뿐이라 무엇 때문에 막혔는지 코드로 분기할 수 없다.
    /// 서버(HTTP)와 같은 코드 표(<see cref="ContextErrorCodes"/>)를 쓴다(T60).
    /// </summary>
    private async Task<T> GuardAsync<T>(string tool, Func<Task<T>> body, T empty) where T : ToolResult
    {
        try
        {
            return await body();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var code = ContextErrorCodes.FromException(exception);
            logger.LogError(exception, "MCP 도구 {Tool} 실패: {Code}", tool, code);
            return empty with { Error = new ToolErrorBody(code, exception.Message) };
        }
    }

    /// <summary>
    /// 규칙 ID 필터. 안내문이 시키는 <c>COMP-*</c>·<c>COMP-</c>와 정확 일치를 모두 받는다.
    ///
    /// 예전에는 정확 일치만 해서 안내문이 시키는 호출이 0건을 돌려줬고,
    /// 에이전트는 "보상 처리 문제 없음"이라고 답했다 — 침묵 오답이다(T60).
    /// </summary>
    internal static bool MatchesRuleFilter(string ruleId, string filter)
    {
        var trimmed = filter.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (trimmed.EndsWith('*'))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.Length == 0 ||
               ruleId.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ruleId, trimmed, StringComparison.OrdinalIgnoreCase);
    }

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
        return await GuardAsync("get_analysis_status", async () =>
        {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var graph = session.Index.Graph;
        var check = await freshnessCache.CheckAsync(graph, cancellationToken);
        var freshness = check.Report;
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
            SuggestedNextTools = SuggestNextTools(graph, freshness.Status.ToString()),
            FreshnessCheckedAt = check.CheckedAt,
            FreshnessCheckDurationMs = check.DurationMs
        };
        }, AnalysisStatusResult.Empty);
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
    public async Task<ContextResult> GetContextAsync(
        [Description("선택 종류: node|endpoint|class|namespace|finding|writePath|middlewareStep|startupStep")] string selectionKind,
        [Description("선택 대상의 ID")] string selectionId,
        [Description("작업 목적: explain|planChange|reviewChange|investigateData|share")] string? intent,
        [Description("길이 예산: short|balanced|detailed")] string? budget,
        [Description("사용자 질문(있으면 문맥 맨 앞에 그대로 들어간다)")] string? question,
        [Description("응답 형태: markdown(기본)|json|both. 둘 다 받으면 같은 내용을 두 번 받는다.")] string? format,
        [Description("인접 호출을 몇 홉까지 담을지(1 또는 2)")] int? neighborDepth,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("get_context", async () =>
        {
        var session = await store.GetSessionAsync(cancellationToken);
        if (!Enum.TryParse<SelectionKind>(selectionKind, ignoreCase: true, out var kind))
        {
            throw new ArgumentException(
                $"지원하지 않는 선택 종류다: {selectionKind}. 쓸 수 있는 값: {string.Join(", ", Enum.GetNames<SelectionKind>())}",
                nameof(selectionKind));
        }

        var document = await new ContextBuilder(new GraphFreshnessEvaluator()).BuildAsync(
            session.Index,
            session.Source,
            new ContextRequest(
                new ContextSelection(kind, selectionId),
                Parse(intent, ContextIntent.Explain),
                Parse(budget, ContextBudget.Balanced))
            {
                Question = question ?? string.Empty,
                ExpectedSnapshotId = expectedSnapshotId,
                NeighborDepth = Math.Clamp(neighborDepth ?? 1, 1, 2)
            },
            cancellationToken);

        // 구조 필드와 그것을 렌더한 Markdown을 함께 주면 같은 내용을 두 번 보낸다.
        // 기본은 Markdown 하나다 — 예산도 실제로 돌려주는 것만 센다(T61).
        var shape = (format ?? "markdown").Trim().ToLowerInvariant();
        return new ContextResult(shape, document.SnapshotId)
        {
            Markdown = shape is "markdown" or "both" ? document.Markdown : null,
            Document = shape is "json" or "both" ? document : null,
            Characters = shape == "json" ? 0 : document.Markdown.Length,
            EstimatedTokens = shape == "json" ? 0 : (document.Markdown.Length + 3) / 4,
            ContainsSource = document.ContainsSource,
            Files = document.Files,
            Limitations = document.Limitations,
            Truncated = document.Truncated
        };
        }, ContextResult.Empty);
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
        return await GuardAsync("get_entrypoint_flow", async () =>
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
        }, EntryPointFlowResult.Empty);
    }

    [McpServerTool(Name = "get_findings")]
    [Description(
        "DB·캐시와 미들웨어 점검 결과를 근거·확실도와 함께 조회한다. " +
        "검사하지 않은 규칙도 카탈로그에 \"꺼짐\"으로 남아 있어 무엇을 보지 않았는지 알 수 있다. " +
        "규칙 ID 접두사(COMP-, TX-, MW-, SHARD-)로 거른다. `COMP-`·`COMP-*`·`COMP-001` 모두 받는다. " +
        "쓰기 경로 안전성은 COMP-로 시작한다. " +
        "Filter by rule id prefix (COMP-, TX-, MW-, SHARD-); write-path safety starts at COMP-.")]
    public async Task<FindingsResult> GetFindingsAsync(
        [Description("이 노드와 연결된 점검만 본다")] string? nodeId,
        [Description("이 규칙 ID나 접두사의 점검만 본다(COMP-, COMP-*, COMP-001)")] string? ruleId,
        [Description("한 번에 돌려줄 최대 개수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("get_findings", async () =>
        {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var graph = session.Index.Graph;
        var all = ((IEnumerable<Finding>)(graph.DataAccess?.Findings ?? []))
            .Concat(graph.Middleware?.Findings ?? [])
            .Where(item => nodeId is null || item.NodeId == nodeId)
            .Where(item => ruleId is null || MatchesRuleFilter(item.RuleId, ruleId))
            .ToArray();
        var page = Page(all, limit, cursor);
        var rules = ((IEnumerable<RuleInfo>)(graph.DataAccess?.Rules ?? []))
            .Concat(graph.Middleware?.Rules ?? [])
            .ToArray();
        var matchedRules = ruleId is null
            ? rules.Select(rule => rule.Id).ToArray()
            : rules.Where(rule => MatchesRuleFilter(rule.Id, ruleId)).Select(rule => rule.Id).ToArray();
        var disabledMatched = rules
            .Where(rule => !rule.Enabled && matchedRules.Contains(rule.Id, StringComparer.Ordinal))
            .ToArray();

        // 0건이 "문제 없음"인지 "그 규칙을 검사하지 않았음"인지 구분할 수 있어야 한다.
        var note = all.Length > 0
            ? null
            : matchedRules.Length == 0
                ? $"`{ruleId}`에 맞는 규칙이 카탈로그에 없다. 규칙 ID를 확인해야 한다."
                : $"매치한 규칙 {matchedRules.Length}개 중 {disabledMatched.Length}개는 이번 분석에서 검사하지 않았다(꺼짐).";

        return new FindingsResult(
            graph.SnapshotId,
            page.Items,
            all.Length,
            page.NextCursor,
            rules.Where(rule => !rule.Enabled).Select(rule => rule.Id).ToArray(),
            rules.Length)
        {
            MatchedRuleIds = matchedRules,
            Note = note
        };
        }, FindingsResult.Empty);
    }

    [McpServerTool(Name = "get_impact")]
    [Description(
        "이 메서드를 바꾸면 영향을 받을 수 있는 HTTP 진입점 후보와 그 근거 경로를 조회한다. " +
        "분석한 호출 관계에서 도출한 후보이며, reflection·외부 호출·이벤트 구독은 증명하지 않는다. " +
        "호출자가 없다고 삭제해도 안전하다는 뜻이 아니다. " +
        "nodeId를 모르면 search_symbol로 먼저 찾아 그 id를 넘긴다. " +
        "If you do not know the nodeId, find it with search_symbol first and pass that id here.")]
    public async Task<ImpactToolResult> GetImpactAsync(
        [Description("영향을 확인할 노드 ID 목록")] string[] nodeIds,
        [Description("역방향 탐색 최대 깊이(기본 12)")] int? maxDepth,
        [Description("방문할 최대 노드 수(기본 4000)")] int? maxNodes,
        [Description("시간 제한(ms, 기본 5000, 최대 60000)")] int? timeoutMs,
        [Description("한 번에 돌려줄 진입점 수(기본 200)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("get_impact", async () =>
        {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var result = new ImpactAnalyzer().Analyze(
            session.Index,
            new ImpactRequest(
                nodeIds,
                Math.Clamp(maxDepth ?? 12, 1, 64),
                Math.Clamp(maxNodes ?? 4000, 1, 20000),
                Math.Clamp(timeoutMs ?? 5000, 100, 60000))
            {
                Limit = Math.Clamp(limit ?? 200, 1, 2000),
                Cursor = Math.Max(0, cursor ?? 0)
            },
            cancellationToken);
        return new ImpactToolResult(result);
        }, ImpactToolResult.Empty);
    }

    [McpServerTool(Name = "compare_snapshots")]
    [Description(
        "보관해 둔 기준 스냅샷과 현재 분석의 구조·본문·점검 차이를 조회한다. " +
        "비교 조건(대상·분석기·규칙·범위)이 다르면 그 사실을 함께 돌려준다. " +
        "\"차이 없음\"은 안전하다는 뜻도, 테스트가 통과한다는 뜻도 아니다. " +
        "수정 전후를 비교할 때만 쓴다. 기준은 사용자가 UI에서 저장해 두어야 있다. " +
        "Use it only to compare before and after a change; the baseline must have been saved by the user.")]
    public async Task<SnapshotDiffResult> CompareSnapshotsAsync(
        [Description("기준 스냅샷 ID(.tracelens/history 아래에 보관한 것)")] string baselineId,
        [Description("비교 대상 스냅샷 ID. 비우면 지금 그래프와 비교한다.")] string? currentId,
        [Description("각 목록에서 돌려줄 최대 항목 수(기본 100)")] int? limit,
        [Description("기대하는 분석 스냅샷 ID. 다르면 오류로 알려준다.")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("compare_snapshots", async () =>
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

        // 상한 없이 돌려주면 수천 줄짜리 차이가 에이전트 문맥을 통째로 먹는다(T61).
        var max = Math.Clamp(limit ?? 100, 1, 2000);
        var diff = new SnapshotComparer().Compare(baseline.Graph, target);
        return SnapshotDiffResult.From(diff, max);
        }, SnapshotDiffResult.Empty);
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
        return await GuardAsync("get_node", async () =>
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
        }, NodeResult.Empty);
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
        [Description("한 번에 읽을 최대 줄 수(기본 400). 넘으면 truncated와 nextStartLine을 함께 준다.")] int? maxLines,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("get_source", async () =>
        {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        // 상한 없이 4000줄을 한 번에 주면 에이전트 문맥이 그것만으로 찬다(T61).
        var lineLimit = Math.Clamp(maxLines ?? DefaultMaxSourceLines, 1, 4000);
        var cappedEnd = Math.Min(endLine, startLine + lineLimit - 1);
        var result = await session.Source.ReadAsync(file, startLine, cappedEnd, cancellationToken);
        if (result.Slice is null)
        {
            return SourceResult.Empty with
            {
                File = file,
                StartLine = startLine,
                EndLine = endLine,
                Denied = ToDenial(result.Access)
            };
        }

        var source = ToSourceResult(result.Slice);
        return cappedEnd < endLine
            ? source with { Truncated = true, NextStartLine = cappedEnd + 1 }
            : source;
        }, SourceResult.Empty);
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
        return await GuardAsync("get_callers", async () =>
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
        }, NeighborResult.Empty);
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
        return await GuardAsync("get_callees", async () =>
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
        }, NeighborResult.Empty);
    }

    [McpServerTool(Name = "get_entrypoints")]
    [Description(
        "현재 TraceLens 그래프의 HTTP 진입점 목록을 조회한다. 같은 메서드에 여러 라우트가 걸리면 각각 나온다. " +
        "\"이 API가 뭘 하나\"의 출발점이다. 고른 endpointId를 get_entrypoint_flow나 get_context에 넘긴다. " +
        "Start here for \"what does this API do\"; pass the endpointId to get_entrypoint_flow or get_context.")]
    public async Task<EntryPointsResult> GetEntryPointsAsync(
        [Description("라우트나 핸들러 이름에 포함된 문자열로 거른다")] string? query,
        [Description("HTTP 동사로 거른다(GET·POST…)")] string? method,
        [Description("라우트 접두사로 거른다(/api/v1)")] string? pathPrefix,
        [Description("실행 프로젝트(어셈블리 이름)로 거른다")] string? projectKey,
        [Description("한 번에 돌려줄 최대 개수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("get_entrypoints", async () =>
        {
        var session = await store.GetSessionAsync(cancellationToken);
        session.EnsureSnapshot(expectedSnapshotId);
        var graph = session.Index.Graph;
        var filtered = graph.EntryPoints
            .Where(item => query is null ||
                           item.Route.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           item.HandlerDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(item => method is null || string.Equals(item.HttpMethod, method, StringComparison.OrdinalIgnoreCase))
            .Where(item => pathPrefix is null || item.Route.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(item => projectKey is null || string.Equals(item.ProjectKey, projectKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var page = Page(filtered, limit, cursor);
        return new EntryPointsResult(
            graph.SnapshotId,
            page.Items,
            filtered.Length,
            page.NextCursor,
            graph.Diagnostics?.EntryPointNotes ?? []);
        }, EntryPointsResult.Empty);
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
        return await GuardAsync("search_symbol", async () =>
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
        }, SearchResult.Empty);
    }

    [McpServerTool(Name = "list_snapshots")]
    [Description(
        "비교 기준으로 보관해 둔 스냅샷 목록을 조회한다. compare_snapshots의 baselineId를 여기서 고른다. " +
        "기준은 사용자가 화면에서 저장해 두어야 있다 — 이 도구는 저장하지 않는다. " +
        "Pick a baselineId here before calling compare_snapshots; this tool never saves one.")]
    public async Task<SnapshotListResult> ListSnapshotsAsync(
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("list_snapshots", async () =>
        {
            var session = await store.GetSessionAsync(cancellationToken);
            session.EnsureSnapshot(expectedSnapshotId);
            var entries = await new SnapshotStore().ListAsync(session.Index.Graph, cancellationToken);
            return new SnapshotListResult(session.SnapshotId, entries);
        }, SnapshotListResult.Empty);
    }

    [McpServerTool(Name = "get_middleware_pipeline")]
    [Description(
        "요청이 지나는 미들웨어 순서와 각 단계의 요청·응답 구간·단락 지점, MVC 필터를 조회한다. " +
        "\"이 요청이 어떤 미들웨어를 지나는가\"에 답한다. 호출 흐름 엣지와는 다른 별도 분석이다. " +
        "Answers \"what does this request pass through\"; it is a separate analysis from the call graph.")]
    public async Task<MiddlewarePipelineResult> GetMiddlewarePipelineAsync(
        [Description("실행 프로젝트(어셈블리 이름). 비우면 첫 번째를 쓴다.")] string? projectKey,
        [Description("한 번에 돌려줄 최대 단계 수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("get_middleware_pipeline", async () =>
        {
            var session = await store.GetSessionAsync(cancellationToken);
            session.EnsureSnapshot(expectedSnapshotId);
            var graph = session.Index.Graph;
            var pipelines = graph.MiddlewarePipelines.Count > 0
                ? graph.MiddlewarePipelines
                : graph.Middleware is null ? [] : new[] { graph.Middleware };
            var pipeline = (projectKey is null
                ? pipelines.FirstOrDefault()
                : pipelines.FirstOrDefault(item =>
                    string.Equals(item.ProjectKey, projectKey, StringComparison.OrdinalIgnoreCase)))
                ?? throw new KeyNotFoundException(projectKey is null
                    ? "미들웨어 파이프라인을 찾지 못했다. Program.cs·Startup.cs를 읽지 못했을 수 있다."
                    : $"프로젝트 {projectKey}의 미들웨어 파이프라인을 찾지 못했다.");

            var page = Page(pipeline.Steps, limit, cursor);
            return new MiddlewarePipelineResult(
                graph.SnapshotId,
                pipeline.ProjectKey,
                pipeline.ProgramFile,
                page.Items,
                pipeline.Steps.Count,
                page.NextCursor,
                pipeline.Filters,
                pipelines.Select(item => item.ProjectKey).ToArray());
        }, MiddlewarePipelineResult.Empty);
    }

    [McpServerTool(Name = "get_startup_flow")]
    [Description(
        "Program.cs의 DI 등록과 미들웨어 파이프라인을 소스 순서대로 조회한다. " +
        "\"이 인터페이스가 어느 구현체로 등록됐나\"를 여기서 확인한다. " +
        "Use it to see how a service is registered and in what order the pipeline is built.")]
    public async Task<StartupFlowResult> GetStartupFlowAsync(
        [Description("실행 프로젝트(어셈블리 이름). 비우면 첫 번째를 쓴다.")] string? projectKey,
        [Description("한 번에 돌려줄 최대 등록 수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("get_startup_flow", async () =>
        {
            var session = await store.GetSessionAsync(cancellationToken);
            session.EnsureSnapshot(expectedSnapshotId);
            var graph = session.Index.Graph;
            var flows = graph.StartupFlows.Count > 0
                ? graph.StartupFlows
                : graph.StartupFlow is null ? [] : new[] { graph.StartupFlow };
            var flow = (projectKey is null
                ? flows.FirstOrDefault()
                : flows.FirstOrDefault(item =>
                    string.Equals(item.ProjectKey, projectKey, StringComparison.OrdinalIgnoreCase)))
                ?? throw new KeyNotFoundException(projectKey is null
                    ? "시작 흐름을 찾지 못했다. Program.cs·Startup.cs를 읽지 못했을 수 있다."
                    : $"프로젝트 {projectKey}의 시작 흐름을 찾지 못했다.");

            var page = Page(flow.ServiceSteps, limit, cursor);
            return new StartupFlowResult(
                graph.SnapshotId,
                flow.ProjectKey,
                flow.ProgramFile,
                page.Items,
                flow.ServiceSteps.Count,
                page.NextCursor,
                flow.MiddlewareSteps,
                flow.Limitations,
                flows.Select(item => item.ProjectKey).ToArray());
        }, StartupFlowResult.Empty);
    }

    [McpServerTool(Name = "get_class")]
    [Description(
        "클래스 하나의 멤버·상속·구현과 각 멤버의 노드 ID를 조회한다. " +
        "여기서 얻은 노드 ID를 get_node·get_impact·get_context에 그대로 넘긴다. " +
        "Pass the member node ids from here to get_node, get_impact or get_context.")]
    public async Task<ClassResult> GetClassAsync(
        [Description("클래스 ID(네임스페이스를 포함한 전체 이름)")] string classId,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("get_class", async () =>
        {
            var session = await store.GetSessionAsync(cancellationToken);
            session.EnsureSnapshot(expectedSnapshotId);
            var index = session.Index;
            var graphClass = index.FindClass(classId)
                ?? throw new KeyNotFoundException($"클래스를 찾을 수 없다: {classId}");
            var nodes = index.Graph.Nodes
                .Where(node => string.Equals(node.ClassId, graphClass.Id, StringComparison.Ordinal))
                .ToArray();
            return new ClassResult(index.SnapshotId, graphClass, nodes);
        }, ClassResult.Empty);
    }

    [McpServerTool(Name = "list_classes")]
    [Description(
        "클래스를 이름·네임스페이스로 찾는다. 클래스 다이어그램에서 보는 목록과 같은 것이다. " +
        "찾은 id를 get_class에 넘겨 멤버와 노드 ID를 얻는다. " +
        "Pass the id to get_class to get members and node ids.")]
    public async Task<ClassListResult> ListClassesAsync(
        [Description("이름이나 전체 ID에 포함된 문자열")] string? query,
        [Description("이 네임스페이스로 시작하는 것만 본다")] string? @namespace,
        [Description("한 번에 돌려줄 최대 개수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("list_classes", async () =>
        {
            var session = await store.GetSessionAsync(cancellationToken);
            session.EnsureSnapshot(expectedSnapshotId);
            var graph = session.Index.Graph;
            var matches = graph.Classes
                .Where(item => query is null ||
                               item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               item.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Where(item => @namespace is null ||
                               item.Namespace.StartsWith(@namespace, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new ClassSummary(
                    item.Id, item.Name, item.Namespace, item.TypeKind.ToString(), item.Members.Count, item.File, item.StartLine))
                .ToArray();
            var page = Page(matches, limit, cursor);
            return new ClassListResult(graph.SnapshotId, page.Items, matches.Length, page.NextCursor);
        }, ClassListResult.Empty);
    }

    [McpServerTool(Name = "list_write_paths")]
    [Description(
        "트랜잭션 없이 저장소를 여러 번 바꾸는 쓰기 경로와 단계별 보상 판정을 조회한다. " +
        "\"이 API의 쓰기 경로가 안전한가\"에 답한다. 판정 근거와 확실도가 함께 온다. " +
        "Answers \"is this write path safe\"; every verdict carries its evidence and confidence.")]
    public async Task<WritePathsResult> ListWritePathsAsync(
        [Description("이 진입점에서 도달하는 경로만 본다(엔드포인트 ID)")] string? entryPointId,
        [Description("한 번에 돌려줄 최대 개수(기본 50, 최대 500)")] int? limit,
        [Description("이어 볼 위치(앞선 응답의 nextCursor)")] int? cursor,
        [Description("기대하는 분석 스냅샷 ID")] string? expectedSnapshotId,
        CancellationToken cancellationToken)
    {
        return await GuardAsync("list_write_paths", async () =>
        {
            var session = await store.GetSessionAsync(cancellationToken);
            session.EnsureSnapshot(expectedSnapshotId);
            var index = session.Index;
            var paths = (IReadOnlyList<WritePath>)(index.Graph.DataAccess?.WritePaths ?? []);

            if (entryPointId is not null)
            {
                var endpoint = index.FindEndpoint(entryPointId)
                    ?? throw new KeyNotFoundException($"진입점을 찾을 수 없다: {entryPointId}");
                var reachable = Reachable(index, endpoint.NodeId);
                paths = paths.Where(path => reachable.Contains(path.MethodNodeId)).ToArray();
            }

            var page = Page(paths, limit, cursor);
            return new WritePathsResult(index.SnapshotId, page.Items, paths.Count, page.NextCursor);
        }, WritePathsResult.Empty);
    }

    /// <summary>진입점에서 전방으로 도달하는 노드. 방문 수에 상한을 둬 넓은 그래프에서도 멈춘다.</summary>
    private static HashSet<string> Reachable(GraphIndex index, string startNodeId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { startNodeId };
        var queue = new Queue<string>();
        queue.Enqueue(startNodeId);
        var visited = 0;
        while (queue.Count > 0 && visited < 4000)
        {
            var current = queue.Dequeue();
            visited++;
            foreach (var callee in index.GetCallees(current))
            {
                if (seen.Add(callee))
                {
                    queue.Enqueue(callee);
                }
            }
        }

        return seen;
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
    bool Truncated) : ToolResult
{
    /// <summary>소스를 읽지 못했을 때의 사유. 읽었으면 null이다(예외 대신 구조로 돌려준다).</summary>
    public SourceDenial? Denied { get; init; }

    /// <summary>
    /// 상한(<c>maxLines</c>) 때문에 잘렸을 때 이어 읽을 시작 줄. 없으면 null이다(T61).
    /// </summary>
    public int? NextStartLine { get; init; }

    public static SourceResult Empty { get; } = new(string.Empty, 0, 0, string.Empty, "unknown", false);
}

public sealed record NodeResult(GraphNode? Node, SourceResult? Source, string SnapshotId) : ToolResult
{
    /// <summary>소스를 읽지 못했을 때의 사유. 노드 메타데이터는 그대로 돌려준다.</summary>
    public SourceDenial? Denied { get; init; }

    public static NodeResult Empty { get; } = new(null, null, string.Empty);
}

public sealed record Neighbor(GraphNode Node, IReadOnlyList<CallSite> CallSites);

/// <param name="Boundaries">확정하지 못한 호출. 목록이 비었다고 "호출 없음"으로 보면 안 된다.</param>
public sealed record NeighborResult(
    string SnapshotId,
    IReadOnlyList<Neighbor> Items,
    int TotalCount,
    int? NextCursor,
    IReadOnlyList<CallSite> Boundaries) : ToolResult
{
    public static NeighborResult Empty { get; } = new(string.Empty, [], 0, null, []);
}

public sealed record SearchResult(
    string SnapshotId,
    IReadOnlyList<GraphNode> Items,
    int TotalCount,
    int? NextCursor) : ToolResult
{
    public static SearchResult Empty { get; } = new(string.Empty, [], 0, null);
}

public sealed record EntryPointsResult(
    string SnapshotId,
    IReadOnlyList<EntryPoint> Items,
    int TotalCount,
    int? NextCursor,
    IReadOnlyList<string> Notes) : ToolResult
{
    public static EntryPointsResult Empty { get; } = new(string.Empty, [], 0, null, []);
}

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
    IReadOnlyList<string> Notes) : ToolResult
{
    public static EntryPointFlowResult Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, [], [], [], false, []);
}

/// <param name="DisabledRuleIds">이번 분석에서 검사하지 않은 규칙. 무엇을 보지 않았는지 알리기 위해 함께 돌려준다.</param>
public sealed record FindingsResult(
    string SnapshotId,
    IReadOnlyList<Finding> Items,
    int TotalCount,
    int? NextCursor,
    IReadOnlyList<string> DisabledRuleIds,
    int RuleCount) : ToolResult
{
    /// <summary>
    /// <c>ruleId</c>가 실제로 어떤 규칙에 매치됐는지. 접두사로 걸렀을 때 무엇을 본 것인지 알 수 있어야 한다(T60).
    /// </summary>
    public IReadOnlyList<string> MatchedRuleIds { get; init; } = [];

    /// <summary>0건일 때의 설명. "문제 없음"과 "그 규칙을 검사하지 않았음"은 다르다.</summary>
    public string? Note { get; init; }

    public static FindingsResult Empty { get; } = new(string.Empty, [], 0, null, [], 0);
}

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
    IReadOnlyList<string> GraphIssues) : ToolResult
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

    /// <summary>신선도를 실제로 계산한 시각. 캐시에서 온 값이면 과거다(T61).</summary>
    public DateTimeOffset FreshnessCheckedAt { get; init; }

    /// <summary>신선도 계산에 걸린 시간(ms). 0이면 캐시에서 돌려준 것이다.</summary>
    public long FreshnessCheckDurationMs { get; init; }

    public static AnalysisStatusResult Empty { get; } = new(
        string.Empty, string.Empty, string.Empty, default, 0, string.Empty, "unknown", [], [], 0, 0, 0, false, null, []);
}

/// <summary>
/// `get_context`의 응답. 기본은 Markdown 하나다 —
/// 구조 필드와 그것을 렌더한 Markdown을 함께 주면 같은 내용을 두 번 보내 에이전트 문맥을 두 배로 쓴다(T61).
/// </summary>
/// <param name="Format">markdown · json · both.</param>
public sealed record ContextResult(string Format, string SnapshotId) : ToolResult
{
    public string? Markdown { get; init; }

    /// <summary>구조 필드 전체. <c>format</c>이 json·both일 때만 채운다.</summary>
    public ContextDocument? Document { get; init; }

    /// <summary>실제로 돌려주는 Markdown의 문자 수. json만 받으면 0이다.</summary>
    public int Characters { get; init; }

    public int EstimatedTokens { get; init; }

    public bool ContainsSource { get; init; }

    public IReadOnlyList<string> Files { get; init; } = [];

    public IReadOnlyList<string> Limitations { get; init; } = [];

    public bool Truncated { get; init; }

    public static ContextResult Empty { get; } = new("markdown", string.Empty);
}

/// <summary>`get_impact`의 응답. 결과 본문은 Context 라이브러리의 것을 그대로 담는다.</summary>
public sealed record ImpactToolResult(ImpactResult? Impact) : ToolResult
{
    public static ImpactToolResult Empty { get; } = new((ImpactResult?)null);
}

/// <summary>
/// `compare_snapshots`의 응답. 목록마다 상한을 두고 전체 수를 함께 준다.
/// </summary>
/// <param name="Totals">상한 전의 항목 수. 목록이 잘렸는지 여기로 안다.</param>
public sealed record SnapshotDiffResult(SnapshotDiff? Diff, DiffTotals? Totals, bool Truncated) : ToolResult
{
    public static SnapshotDiffResult Empty { get; } = new(null, null, false);

    public static SnapshotDiffResult From(SnapshotDiff diff, int max)
    {
        var totals = new DiffTotals(
            diff.Endpoints.Count,
            diff.Methods.Count,
            diff.Edges.Count,
            diff.DataAccess.Count,
            diff.Findings.Count,
            diff.Middleware.Count,
            diff.StartupFlow.Count,
            diff.WritePaths.Count);
        var truncated =
            diff.Endpoints.Count > max || diff.Methods.Count > max || diff.Edges.Count > max ||
            diff.DataAccess.Count > max || diff.Findings.Count > max || diff.Middleware.Count > max ||
            diff.StartupFlow.Count > max || diff.WritePaths.Count > max;

        return new SnapshotDiffResult(
            diff with
            {
                Endpoints = diff.Endpoints.Take(max).ToArray(),
                Methods = diff.Methods.Take(max).ToArray(),
                Edges = diff.Edges.Take(max).ToArray(),
                DataAccess = diff.DataAccess.Take(max).ToArray(),
                Findings = diff.Findings.Take(max).ToArray(),
                RenameCandidates = diff.RenameCandidates.Take(max).ToArray(),
                Middleware = diff.Middleware.Take(max).ToArray(),
                StartupFlow = diff.StartupFlow.Take(max).ToArray(),
                WritePaths = diff.WritePaths.Take(max).ToArray()
            },
            totals,
            truncated);
    }
}

/// <summary>상한 전의 항목 수.</summary>
public sealed record DiffTotals(
    int Endpoints,
    int Methods,
    int Edges,
    int DataAccess,
    int Findings,
    int Middleware,
    int StartupFlow,
    int WritePaths);

/// <summary>`list_snapshots`의 응답.</summary>
public sealed record SnapshotListResult(string SnapshotId, IReadOnlyList<SnapshotEntry> Items) : ToolResult
{
    public static SnapshotListResult Empty { get; } = new(string.Empty, []);
}

/// <param name="ProjectKeys">이 솔루션에서 고를 수 있는 실행 프로젝트. 여럿이면 projectKey로 지정한다.</param>
public sealed record MiddlewarePipelineResult(
    string SnapshotId,
    string ProjectKey,
    string ProgramFile,
    IReadOnlyList<MiddlewareStepInfo> Steps,
    int TotalSteps,
    int? NextCursor,
    IReadOnlyList<MvcFilterInfo> Filters,
    IReadOnlyList<string> ProjectKeys) : ToolResult
{
    public static MiddlewarePipelineResult Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, [], 0, null, [], []);
}

public sealed record StartupFlowResult(
    string SnapshotId,
    string ProjectKey,
    string ProgramFile,
    IReadOnlyList<StartupStep> ServiceSteps,
    int TotalServiceSteps,
    int? NextCursor,
    IReadOnlyList<StartupStep> MiddlewareSteps,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> ProjectKeys) : ToolResult
{
    public static StartupFlowResult Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, [], 0, null, [], [], []);
}

/// <param name="Nodes">이 클래스의 멤버 중 그래프 노드가 있는 것. id를 다른 도구에 그대로 넘긴다.</param>
public sealed record ClassResult(string SnapshotId, GraphClass? Class, IReadOnlyList<GraphNode> Nodes) : ToolResult
{
    public static ClassResult Empty { get; } = new(string.Empty, null, []);
}

public sealed record ClassSummary(
    string Id,
    string Name,
    string Namespace,
    string TypeKind,
    int MemberCount,
    string File,
    int StartLine);

public sealed record ClassListResult(
    string SnapshotId,
    IReadOnlyList<ClassSummary> Items,
    int TotalCount,
    int? NextCursor) : ToolResult
{
    public static ClassListResult Empty { get; } = new(string.Empty, [], 0, null);
}

public sealed record WritePathsResult(
    string SnapshotId,
    IReadOnlyList<WritePath> Items,
    int TotalCount,
    int? NextCursor) : ToolResult
{
    public static WritePathsResult Empty { get; } = new(string.Empty, [], 0, null);
}

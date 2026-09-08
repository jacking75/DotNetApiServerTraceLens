using System.Diagnostics;
using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

public sealed record ImpactRequest(
    IReadOnlyList<string> NodeIds,
    int MaxDepth = 12,
    int MaxNodes = 4000,
    int TimeoutMs = 5000);

/// <param name="Resolution">이 단계의 호출 근거. 근거가 없으면 <c>unknown</c>이다.</param>
public sealed record ImpactPathStep(
    string NodeId,
    string DisplayName,
    string File,
    int StartLine,
    string Resolution,
    string Confidence);

public sealed record ImpactEndpoint(
    string EndpointId,
    string HttpMethod,
    string Route,
    string HandlerNodeId,
    string HandlerDisplayName,
    int Distance,
    string Confidence,
    IReadOnlyList<ImpactPathStep> Path);

/// <param name="TestCandidates">그래프에 들어 있는 테스트 메서드에서 도달한 경우만 담는다.</param>
public sealed record ImpactResult(
    string SnapshotId,
    IReadOnlyList<string> Targets,
    IReadOnlyList<ImpactEndpoint> Endpoints,
    IReadOnlyList<string> UnreachableTargets,
    IReadOnlyList<ImpactTestCandidate> TestCandidates,
    bool Truncated,
    IReadOnlyList<string> TruncationReasons,
    IReadOnlyList<string> Limitations,
    int VisitedNodes);

/// <param name="Evidence">reached면 그래프에서 도달했다는 뜻이고, nameMatch면 이름만 비슷하다는 뜻이다.</param>
public sealed record ImpactTestCandidate(string NodeId, string DisplayName, string File, int StartLine, string Evidence);

/// <summary>
/// 선택한 메서드에서 <b>역방향</b>으로 넓혀 도달하는 HTTP 진입점을 찾는다.
/// 결과는 "분석한 메서드 호출 관계에서 도출한 영향 후보"이며,
/// reflection·외부 호출·이벤트 구독·런타임 DI 선택까지 증명하지 않는다.
/// 호출자가 없다고 "삭제해도 안전"이라고 말하지 않는다.
/// </summary>
public sealed class ImpactAnalyzer
{
    private static readonly string[] TestNameHints = ["Test", "Tests", "Should", "Spec", "Fixture"];

    public ImpactResult Analyze(GraphIndex index, ImpactRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var targets = request.NodeIds.Where(id => index.FindNode(id) is not null).Distinct(StringComparer.Ordinal).ToArray();
        var missing = request.NodeIds.Except(targets, StringComparer.Ordinal).ToArray();

        var endpoints = new Dictionary<string, ImpactEndpoint>(StringComparer.Ordinal);
        var truncationReasons = new List<string>();
        var truncated = false;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var testCandidates = new Dictionary<string, ImpactTestCandidate>(StringComparer.Ordinal);
        var unreachable = new List<string>();

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 진입점마다 대표 최단 경로 하나만 만든다. 모든 경로를 열거하지 않는다.
            var cameFrom = new Dictionary<string, string>(StringComparer.Ordinal);
            var depthByNode = new Dictionary<string, int>(StringComparer.Ordinal) { [target] = 0 };
            var queue = new Queue<string>();
            queue.Enqueue(target);
            var found = false;

            while (queue.Count > 0)
            {
                if (visited.Count >= request.MaxNodes)
                {
                    truncated = true;
                    AddReason(truncationReasons, $"방문 노드 상한 {request.MaxNodes}개에 도달해 탐색을 멈췄다.");
                    break;
                }

                if (stopwatch.ElapsedMilliseconds > request.TimeoutMs)
                {
                    truncated = true;
                    AddReason(truncationReasons, $"제한 시간 {request.TimeoutMs}ms에 도달해 탐색을 멈췄다.");
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var current = queue.Dequeue();
                visited.Add(current);
                var depth = depthByNode[current];

                foreach (var endpoint in index.GetEndpointsForNode(current))
                {
                    found = true;
                    var key = GraphIndex.EndpointKey(endpoint);
                    if (endpoints.TryGetValue(key, out var existing) && existing.Distance <= depth)
                    {
                        continue;
                    }

                    var path = BuildPath(index, cameFrom, current, target);
                    endpoints[key] = new ImpactEndpoint(
                        key,
                        endpoint.HttpMethod,
                        endpoint.Route,
                        endpoint.NodeId,
                        string.IsNullOrEmpty(endpoint.HandlerDisplayName)
                            ? index.FindNode(endpoint.NodeId)?.DisplayName ?? endpoint.NodeId
                            : endpoint.HandlerDisplayName,
                        depth,
                        path.Any(step => step.Confidence == nameof(CallConfidence.Estimated))
                            ? nameof(CallConfidence.Estimated)
                            : endpoint.Confidence.ToString(),
                        path);
                }

                if (IsTestNode(index, current) && current != target)
                {
                    var node = index.FindNode(current)!;
                    testCandidates[current] = new ImpactTestCandidate(
                        node.Id, node.DisplayName, node.File, node.StartLine, "reached");
                }

                if (depth >= request.MaxDepth)
                {
                    truncated = true;
                    AddReason(truncationReasons, $"깊이 상한 {request.MaxDepth}에 도달한 경로가 있다.");
                    continue;
                }

                foreach (var caller in index.GetCallers(current))
                {
                    if (depthByNode.ContainsKey(caller))
                    {
                        continue;
                    }

                    depthByNode[caller] = depth + 1;
                    cameFrom[caller] = current;
                    queue.Enqueue(caller);
                }
            }

            if (!found)
            {
                unreachable.Add(target);
            }
        }

        foreach (var candidate in FindTestCandidatesByName(index, targets))
        {
            testCandidates.TryAdd(candidate.NodeId, candidate);
        }

        var limitations = BuildLimitations(index, unreachable.Count > 0, testCandidates.Count == 0);
        return new ImpactResult(
            index.SnapshotId,
            targets,
            endpoints.Values
                .OrderBy(item => item.Distance)
                .ThenBy(item => item.Route, StringComparer.Ordinal)
                .ThenBy(item => item.HttpMethod, StringComparer.Ordinal)
                .ToArray(),
            unreachable,
            testCandidates.Values.OrderBy(item => item.DisplayName, StringComparer.Ordinal).ToArray(),
            truncated,
            truncationReasons,
            limitations,
            visited.Count);
    }

    private static IReadOnlyList<ImpactPathStep> BuildPath(
        GraphIndex index,
        IReadOnlyDictionary<string, string> cameFrom,
        string entryNodeId,
        string target)
    {
        // 역방향 BFS라 cameFrom은 "호출자 → 피호출자" 방향이다. 진입점에서 대상까지 그대로 따라가면 된다.
        var steps = new List<ImpactPathStep>();
        var current = entryNodeId;
        var guard = 0;
        while (true)
        {
            var node = index.FindNode(current);
            if (node is null || guard++ > 200)
            {
                break;
            }

            var next = cameFrom.TryGetValue(current, out var value) ? value : null;
            var site = next is null ? null : index.GetCallSites(current, next).FirstOrDefault();
            steps.Add(new ImpactPathStep(
                node.Id,
                node.DisplayName,
                node.File,
                node.StartLine,
                site?.Resolution.ToString() ?? "unknown",
                site?.Confidence.ToString() ?? nameof(CallConfidence.Estimated)));
            if (current == target || next is null)
            {
                break;
            }

            current = next;
        }

        return steps;
    }

    private static bool IsTestNode(GraphIndex index, string nodeId)
    {
        var node = index.FindNode(nodeId);
        if (node is null)
        {
            return false;
        }

        return TestNameHints.Any(hint =>
            node.ClassId.Contains(hint, StringComparison.OrdinalIgnoreCase) ||
            node.Namespace.Contains("Test", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>이름만 비슷한 후보. 도달 관계가 없으므로 근거가 약하다는 것을 값으로 남긴다.</summary>
    private static IEnumerable<ImpactTestCandidate> FindTestCandidatesByName(GraphIndex index, IReadOnlyList<string> targets)
    {
        var names = targets
            .Select(id => index.FindNode(id)?.DisplayName.Split('.').Last())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToArray();
        if (names.Length == 0)
        {
            yield break;
        }

        foreach (var node in index.Nodes)
        {
            if (!IsTestNode(index, node.Id))
            {
                continue;
            }

            if (names.Any(name => node.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                yield return new ImpactTestCandidate(node.Id, node.DisplayName, node.File, node.StartLine, "nameMatch");
            }
        }
    }

    private static IReadOnlyList<string> BuildLimitations(GraphIndex index, bool hasUnreachable, bool noTests)
    {
        var limitations = new List<string>
        {
            "이 목록은 분석한 메서드 호출 관계에서 도출한 영향 후보다. reflection·외부 호출·이벤트 구독·런타임 DI 선택은 증명하지 않는다.",
            "호출자가 발견되지 않았다고 해서 삭제해도 안전하다는 뜻은 아니다.",
            "프로퍼티·DTO 형태 변경 같은 타입·계약 영향은 아직 다루지 않는다."
        };

        var diagnostics = index.Graph.Diagnostics;
        if (diagnostics is not null)
        {
            if (string.Equals(diagnostics.AnalysisScope, "entrypoints", StringComparison.OrdinalIgnoreCase))
            {
                limitations.Add("analysisScope=entryPoints로 분석해서 진입점에서 도달하지 못하는 호출자는 그래프에 없다.");
            }

            if (diagnostics.DepthLimitReached)
            {
                limitations.Add($"분석 깊이 제한(maxCallDepth={diagnostics.MaxCallDepth})에 걸린 경로가 있어 결과가 불완전할 수 있다.");
            }

            if (diagnostics.ProjectsWithCompileErrors > 0)
            {
                limitations.Add($"컴파일 오류가 있는 프로젝트가 {diagnostics.ProjectsWithCompileErrors}개라 호출 관계가 빠졌을 수 있다.");
            }
        }

        if (hasUnreachable)
        {
            limitations.Add("도달하는 HTTP 진입점을 찾지 못한 대상이 있다. 배치·메시지 소비자처럼 다른 경로로 실행될 수 있다.");
        }

        if (noTests)
        {
            limitations.Add("관련 테스트는 미조사다. 후보 없음과 테스트 커버리지 충족은 다른 말이다.");
        }

        return limitations;
    }

    private static void AddReason(List<string> reasons, string reason)
    {
        if (!reasons.Contains(reason, StringComparer.Ordinal))
        {
            reasons.Add(reason);
        }
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>진입점에서 시작해 <see cref="TraceLensConfig.MaxCallDepth"/>까지 호출 그래프를 넓힌다.</summary>
public sealed class CallGraphWalker(
    NodeClassifier nodeClassifier,
    InvocationResolver invocationResolver,
    ILogger<CallGraphWalker> logger)
{
    public async Task<CallGraphResult> WalkAsync(
        LoadedSolution loaded,
        IReadOnlyList<ScannedEntryPoint> entryPoints,
        TraceLensConfig config,
        string solutionDirectory,
        CancellationToken cancellationToken = default)
    {
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var edges = new EdgeCollector();
        var visitedDepth = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<(AnalysisCallable Callable, int Depth)>();
        var collector = new CallSiteCollector(config.IncludeCallSites, config.MaxCallSitesPerEdge, config.MaxBoundaryCallSites);
        var depthLimitReached = false;

        // 진입점 핸들러로 등록된 람다는 자기 노드가 있으므로, 그 람다를 감싼 메서드를 훑을 때 건너뛴다.
        var registeredLambdas = entryPoints
            .Select(item => item.Handler)
            .Where(item => item.Kind == CallableKind.Lambda)
            .Select(item => LambdaKey.For(item.Declaration))
            .ToHashSet();

        foreach (var entryPoint in entryPoints)
        {
            queue.Enqueue((entryPoint.Handler, 0));
        }

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (callable, depth) = queue.Dequeue();
            var methodId = callable.NodeId;
            if (visitedDepth.TryGetValue(methodId, out var existingDepth) && existingDepth <= depth)
            {
                continue;
            }

            visitedDepth[methodId] = depth;
            var node = GraphNodes.Create(callable, solutionDirectory, nodeClassifier.Classify(callable.ContainingType, config));
            if (node is null)
            {
                continue;
            }

            nodes[methodId] = node;
            if (depth >= config.MaxCallDepth)
            {
                depthLimitReached = true;
                logger.LogDebug("maxCallDepth에 도달해 더 내려가지 않는다: {Method}", methodId);
                continue;
            }

            foreach (var call in await GetCallsAsync(
                         loaded, callable, solutionDirectory, config, registeredLambdas, cancellationToken))
            {
                if (!call.IsEdge || call.Target is null)
                {
                    collector.Add(methodId, null, call);
                    continue;
                }

                var calledCallable = await CreateCallableAsync(call.Target, cancellationToken);
                if (calledCallable is null)
                {
                    collector.Add(methodId, null, call with
                    {
                        Resolution = CallResolution.Unresolved,
                        Note = "대상 메서드의 선언 위치를 찾지 못했다."
                    });
                    continue;
                }

                var calledNode = GraphNodes.Create(
                    calledCallable,
                    solutionDirectory,
                    nodeClassifier.Classify(calledCallable.ContainingType, config));
                if (calledNode is null)
                {
                    collector.Add(methodId, null, call with
                    {
                        Resolution = CallResolution.External,
                        Note = "대상 선언이 분석 루트 밖이거나 빌드 산출물이다."
                    });
                    continue;
                }

                nodes[calledCallable.NodeId] = calledNode;
                edges.Add(methodId, calledCallable.NodeId, call.Confidence);
                collector.Add(methodId, calledCallable.NodeId, call);
                queue.Enqueue((calledCallable, depth + 1));
            }
        }

        var nodeIds = nodes.Keys.ToHashSet(StringComparer.Ordinal);
        var builtEdges = edges.Build(nodeIds);
        return new CallGraphResult(
            nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray(),
            builtEdges,
            collector.Build(nodeIds),
            // 해석률의 분자는 실제로 엣지가 된 것만 센다. 버린 엣지를 세면 분자가 부풀려진다(T58).
            collector.Stats with { DroppedEdges = edges.DroppedForMissingNode },
            depthLimitReached,
            collector.DroppedForLimit,
            edges.DroppedForMissingNode);
    }

    private async Task<IReadOnlyList<ResolvedCall>> GetCallsAsync(
        LoadedSolution loaded,
        AnalysisCallable callable,
        string solutionDirectory,
        TraceLensConfig config,
        IReadOnlySet<LambdaKey> registeredLambdas,
        CancellationToken cancellationToken)
    {
        var syntax = callable.Declaration;

        // 주 생성자는 선언 위치가 타입 자체라서, 그대로 훑으면 클래스 안 모든 호출이 이 생성자의 호출로 잡힌다.
        if (syntax is TypeDeclarationSyntax)
        {
            return [];
        }

        var document = loaded.Solution.GetDocument(syntax.SyntaxTree);
        if (document is null)
        {
            return [];
        }

        var model = (await loaded.GetSemanticsAsync(document, cancellationToken))?.Model;
        return model is null
            ? []
            : invocationResolver.ResolveCalls(
                model, syntax, solutionDirectory, config, cancellationToken, registeredLambdas);
    }

    /// <summary>호출 대상 심볼로 callable을 만든다. 기본 생성자는 선언 구문이 없으므로 그 타입 선언 위치를 쓴다.</summary>
    private static async Task<AnalysisCallable?> CreateCallableAsync(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        var reference = InvocationResolver.GetDeclarations(method).FirstOrDefault();
        if (reference is null)
        {
            return null;
        }

        var syntax = await reference.GetSyntaxAsync(cancellationToken);
        var projectKey = method.ContainingAssembly?.Name ?? string.Empty;
        return method.MethodKind == MethodKind.LocalFunction
            ? AnalysisCallable.ForLocalFunction(method, syntax, projectKey, null)
            : AnalysisCallable.ForMethod(method, syntax, projectKey);
    }
}

/// <param name="DroppedCallSites">상한 때문에 근거 목록에서 뺀 호출 지점 수.</param>
/// <param name="DroppedEdges">대상 노드가 그래프에 없어 넣지 못한 엣지 수.</param>
public sealed record CallGraphResult(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<CallSite> CallSites,
    CallResolutionStats Stats,
    bool DepthLimitReached,
    int DroppedCallSites,
    int DroppedEdges);

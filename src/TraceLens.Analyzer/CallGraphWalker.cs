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
        var edges = new HashSet<GraphEdge>();
        var visitedDepth = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<(IMethodSymbol Method, int Depth)>();

        foreach (var entryPoint in entryPoints)
        {
            queue.Enqueue((entryPoint.Method, 0));
        }

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (method, depth) = queue.Dequeue();
            var methodId = SymbolNames.GetNodeId(method);
            if (visitedDepth.TryGetValue(methodId, out var existingDepth) && existingDepth <= depth)
            {
                continue;
            }

            visitedDepth[methodId] = depth;
            var node = await CreateNodeAsync(method, solutionDirectory, config, cancellationToken);
            if (node is null)
            {
                continue;
            }

            nodes[methodId] = node;
            if (depth >= config.MaxCallDepth)
            {
                logger.LogDebug("maxCallDepth에 도달해 더 내려가지 않는다: {Method}", methodId);
                continue;
            }

            foreach (var calledMethod in await GetCalledMethodsAsync(
                         loaded.Solution,
                         method,
                         solutionDirectory,
                         config,
                         cancellationToken))
            {
                var calledId = SymbolNames.GetNodeId(calledMethod);
                var calledNode = await CreateNodeAsync(calledMethod, solutionDirectory, config, cancellationToken);
                if (calledNode is null)
                {
                    continue;
                }

                nodes[calledId] = calledNode;
                edges.Add(new GraphEdge(methodId, calledId));
                queue.Enqueue((calledMethod, depth + 1));
            }
        }

        return new CallGraphResult(
            nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray(),
            edges.OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal).ToArray());
    }

    private async Task<IReadOnlyList<IMethodSymbol>> GetCalledMethodsAsync(
        Solution solution,
        IMethodSymbol method,
        string solutionDirectory,
        TraceLensConfig config,
        CancellationToken cancellationToken)
    {
        var result = new List<IMethodSymbol>();
        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            var syntax = await syntaxReference.GetSyntaxAsync(cancellationToken);

            // 주 생성자는 선언 위치가 타입 자체라서, 그대로 훑으면 클래스 안 모든 호출이 이 생성자의 호출로 잡힌다.
            if (syntax is TypeDeclarationSyntax)
            {
                continue;
            }

            var document = solution.GetDocument(syntax.SyntaxTree);
            if (document is null)
            {
                continue;
            }

            var model = await document.GetSemanticModelAsync(cancellationToken);
            if (model is null)
            {
                continue;
            }

            result.AddRange(invocationResolver.ResolveCalls(model, syntax, solutionDirectory, config, cancellationToken));
        }

        return result;
    }

    private async Task<GraphNode?> CreateNodeAsync(
        IMethodSymbol method,
        string solutionDirectory,
        TraceLensConfig config,
        CancellationToken cancellationToken)
    {
        // 기본 생성자는 선언 구문이 없으므로 그 타입 선언 위치를 쓴다.
        var reference = InvocationResolver.GetDeclarations(method).FirstOrDefault();
        if (reference is null)
        {
            return null;
        }

        var syntax = await reference.GetSyntaxAsync(cancellationToken);
        return GraphNodes.Create(method, syntax, solutionDirectory, nodeClassifier.Classify(method, config));
    }
}

public sealed record CallGraphResult(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

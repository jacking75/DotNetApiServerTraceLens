using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

public sealed class CallGraphWalker(
    DiResolver diResolver,
    NodeClassifier nodeClassifier,
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
                continue;
            }

            foreach (var calledMethod in await GetCalledMethodsAsync(loaded.Solution, method, cancellationToken))
            {
                var resolvedMethod = calledMethod.ContainingType.TypeKind == TypeKind.Interface
                    ? diResolver.Resolve(calledMethod)
                    : calledMethod;
                if (resolvedMethod is null || !IsSolutionMethod(resolvedMethod, solutionDirectory, config))
                {
                    continue;
                }

                var calledId = SymbolNames.GetNodeId(resolvedMethod);
                var calledNode = await CreateNodeAsync(resolvedMethod, solutionDirectory, config, cancellationToken);
                if (calledNode is null)
                {
                    continue;
                }

                nodes[calledId] = calledNode;
                edges.Add(new GraphEdge(methodId, calledId));
                queue.Enqueue((resolvedMethod, depth + 1));
            }
        }

        return new CallGraphResult(
            nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray(),
            edges.OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal).ToArray());
    }

    private async Task<IReadOnlyList<IMethodSymbol>> GetCalledMethodsAsync(
        Solution solution,
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        var result = new List<IMethodSymbol>();
        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            var syntax = await syntaxReference.GetSyntaxAsync(cancellationToken);
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

            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var symbolInfo = model.GetSymbolInfo(invocation, cancellationToken);
                var calledMethod = symbolInfo.Symbol as IMethodSymbol ??
                                   symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                if (calledMethod is not null)
                {
                    result.Add(calledMethod.ReducedFrom ?? calledMethod);
                }
                else
                {
                    logger.LogDebug("호출 심볼을 해석하지 못했다: {Invocation}", invocation);
                }
            }
        }

        return result;
    }

    private async Task<GraphNode?> CreateNodeAsync(
        IMethodSymbol method,
        string solutionDirectory,
        TraceLensConfig config,
        CancellationToken cancellationToken)
    {
        var reference = method.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference is null)
        {
            return null;
        }

        var syntax = await reference.GetSyntaxAsync(cancellationToken);
        var filePath = syntax.SyntaxTree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var relativePath = Path.GetRelativePath(solutionDirectory, Path.GetFullPath(filePath));
        if (relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        var lineSpan = syntax.GetLocation().GetLineSpan();
        return new GraphNode(
            SymbolNames.GetNodeId(method),
            nodeClassifier.Classify(method, config),
            SymbolNames.GetDisplayName(method),
            method.ContainingNamespace.IsGlobalNamespace ? string.Empty : method.ContainingNamespace.ToDisplayString(),
            relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.EndLinePosition.Line + 1);
    }

    private static bool IsSolutionMethod(IMethodSymbol method, string solutionDirectory, TraceLensConfig config)
    {
        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (config.ExcludeNamespacePrefixes.Any(prefix =>
                namespaceName.Equals(prefix, StringComparison.Ordinal) ||
                namespaceName.StartsWith(prefix + ".", StringComparison.Ordinal)))
        {
            return false;
        }

        return method.DeclaringSyntaxReferences.Any(reference =>
        {
            var path = reference.SyntaxTree.FilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var relativePath = Path.GetRelativePath(solutionDirectory, Path.GetFullPath(path));
            return relativePath != ".." && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        });
    }
}

public sealed record CallGraphResult(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

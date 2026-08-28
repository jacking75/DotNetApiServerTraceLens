using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 솔루션 안에 선언된 모든 클래스(레코드/구조체 포함)와 그 메서드를 문서 단위로 한 번씩 훑는다.
/// 진입점에서 도달하지 못하는 클래스도 목록에 넣기 위해 항상 실행하고,
/// <see cref="AnalysisScope.Solution"/>일 때는 같은 순회에서 호출 엣지까지 모은다.
/// </summary>
public sealed class SolutionScanner(
    NodeClassifier nodeClassifier,
    InvocationResolver invocationResolver,
    ILogger<SolutionScanner> logger)
{
    public async Task<SolutionScanResult> ScanAsync(
        LoadedSolution loaded,
        TraceLensConfig config,
        string solutionDirectory,
        bool collectCalls,
        CancellationToken cancellationToken = default)
    {
        var classes = new Dictionary<string, ClassBuilder>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var edges = new HashSet<GraphEdge>();

        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            foreach (var document in project.Documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = SourcePaths.ToSolutionRelative(solutionDirectory, document.FilePath);
                if (relativePath is null || SourcePaths.IsBuildOutput(relativePath))
                {
                    continue;
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                var model = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || model is null)
                {
                    continue;
                }

                foreach (var typeSyntax in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (typeSyntax is InterfaceDeclarationSyntax)
                    {
                        continue;
                    }

                    if (model.GetDeclaredSymbol(typeSyntax, cancellationToken) is not INamedTypeSymbol type)
                    {
                        logger.LogDebug("타입 심볼을 해석하지 못했다: {File}", relativePath);
                        continue;
                    }

                    if (IsExcludedNamespace(type, config))
                    {
                        continue;
                    }

                    var classId = SymbolNames.GetClassId(type);
                    if (!classes.TryGetValue(classId, out var builder))
                    {
                        var typeLineSpan = typeSyntax.GetLocation().GetLineSpan();
                        builder = new ClassBuilder(
                            type,
                            classId,
                            nodeClassifier.Classify(type, config),
                            relativePath,
                            typeLineSpan.StartLinePosition.Line + 1,
                            typeLineSpan.EndLinePosition.Line + 1);
                        classes.Add(classId, builder);
                    }

                    foreach (var methodSyntax in typeSyntax.Members.OfType<MethodDeclarationSyntax>())
                    {
                        if (model.GetDeclaredSymbol(methodSyntax, cancellationToken) is not IMethodSymbol method)
                        {
                            continue;
                        }

                        var node = GraphNodes.Create(method, methodSyntax, solutionDirectory, builder.Kind);
                        if (node is null)
                        {
                            continue;
                        }

                        builder.AddMethod(node.Id);
                        if (!collectCalls)
                        {
                            continue;
                        }

                        nodes[node.Id] = node;
                        foreach (var called in invocationResolver.ResolveCalls(
                                     model,
                                     methodSyntax,
                                     solutionDirectory,
                                     config,
                                     cancellationToken))
                        {
                            edges.Add(new GraphEdge(node.Id, SymbolNames.GetNodeId(called)));
                        }
                    }
                }
            }
        }

        logger.LogInformation("클래스 {ClassCount}개를 찾았다.", classes.Count);
        return new SolutionScanResult(
            classes.Values
                .Select(builder => builder.Build())
                .OrderBy(item => item.Namespace, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ToArray(),
            nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray(),
            edges
                .Where(edge => nodes.ContainsKey(edge.From) && nodes.ContainsKey(edge.To))
                .OrderBy(edge => edge.From, StringComparer.Ordinal)
                .ThenBy(edge => edge.To, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool IsExcludedNamespace(INamedTypeSymbol type, TraceLensConfig config)
    {
        var namespaceName = type.ContainingNamespace?.IsGlobalNamespace == false
            ? type.ContainingNamespace.ToDisplayString()
            : string.Empty;
        return config.ExcludeNamespacePrefixes.Any(prefix =>
            namespaceName.Equals(prefix, StringComparison.Ordinal) ||
            namespaceName.StartsWith(prefix + ".", StringComparison.Ordinal));
    }

    private sealed class ClassBuilder(
        INamedTypeSymbol symbol,
        string id,
        NodeKind kind,
        string file,
        int startLine,
        int endLine)
    {
        private readonly SortedSet<string> methodIds = new(StringComparer.Ordinal);

        public NodeKind Kind { get; } = kind;

        public void AddMethod(string methodId) => methodIds.Add(methodId);

        public GraphClass Build() => new(
            id,
            SymbolNames.GetClassDisplayName(symbol),
            symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
            Kind,
            file,
            startLine,
            endLine,
            methodIds.ToArray());
    }
}

public sealed record SolutionScanResult(
    IReadOnlyList<GraphClass> Classes,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges);

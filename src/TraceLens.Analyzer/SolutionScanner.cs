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
        var pendingConstructors = new Dictionary<string, GraphNode>(StringComparer.Ordinal);

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

                    foreach (var (memberSyntax, method) in EnumerateMembers(typeSyntax, model, cancellationToken))
                    {
                        var node = GraphNodes.Create(method, memberSyntax, solutionDirectory, builder.Kind);
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
                                     memberSyntax,
                                     solutionDirectory,
                                     config,
                                     cancellationToken))
                        {
                            edges.Add(new GraphEdge(node.Id, SymbolNames.GetNodeId(called)));
                            QueueConstructorNode(called, solutionDirectory, config, nodes, pendingConstructors, cancellationToken);
                        }
                    }
                }
            }
        }

        // 기본 생성자로 만든 노드는 클래스 목록이 다 채워진 뒤에 붙인다.
        foreach (var (id, node) in pendingConstructors)
        {
            if (nodes.ContainsKey(id) || !classes.TryGetValue(node.ClassId, out var builder))
            {
                continue;
            }

            nodes[id] = node;
            builder.AddMethod(id);
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

    /// <summary>노드로 만들 멤버를 돌려준다. 메서드와 명시적 생성자, 그리고 본문이 있는 프로퍼티를 대상으로 한다.</summary>
    private static IEnumerable<(SyntaxNode Syntax, IMethodSymbol Method)> EnumerateMembers(
        TypeDeclarationSyntax typeSyntax,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var member in typeSyntax.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax methodSyntax
                    when model.GetDeclaredSymbol(methodSyntax, cancellationToken) is IMethodSymbol method:
                    yield return (methodSyntax, method);
                    break;

                case ConstructorDeclarationSyntax constructorSyntax
                    when model.GetDeclaredSymbol(constructorSyntax, cancellationToken) is IMethodSymbol constructor:
                    yield return (constructorSyntax, constructor);
                    break;

                // 자동 프로퍼티는 실행할 코드가 없으므로 본문이 있는 프로퍼티만 노드로 만든다.
                case PropertyDeclarationSyntax propertySyntax when HasBody(propertySyntax):
                    var property = model.GetDeclaredSymbol(propertySyntax, cancellationToken) as IPropertySymbol;
                    var accessor = property?.GetMethod ?? property?.SetMethod;
                    if (accessor is not null)
                    {
                        yield return (propertySyntax, accessor);
                    }

                    break;
            }
        }
    }

    private static bool HasBody(PropertyDeclarationSyntax property) =>
        property.ExpressionBody is not null ||
        (property.AccessorList?.Accessors.Any(accessor =>
            accessor.Body is not null || accessor.ExpressionBody is not null) ?? false);

    /// <summary>
    /// <c>new X()</c>가 가리키는 생성자는 선언을 훑는 단계에서 노드가 안 만들어질 수 있어서(컴파일러가 만드는 기본 생성자)
    /// 따로 모아 뒀다가 클래스 목록이 완성된 뒤에 붙인다.
    /// </summary>
    private void QueueConstructorNode(
        IMethodSymbol called,
        string solutionDirectory,
        TraceLensConfig config,
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, GraphNode> pendingConstructors,
        CancellationToken cancellationToken)
    {
        if (called.MethodKind != MethodKind.Constructor)
        {
            return;
        }

        var id = SymbolNames.GetNodeId(called);
        if (nodes.ContainsKey(id) || pendingConstructors.ContainsKey(id))
        {
            return;
        }

        var node = GraphNodes.CreateFromSymbol(
            called,
            solutionDirectory,
            nodeClassifier.Classify(called.ContainingType, config),
            cancellationToken);
        if (node is not null)
        {
            pendingConstructors[id] = node;
        }
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

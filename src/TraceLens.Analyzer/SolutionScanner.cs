using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 솔루션 안에 선언된 모든 타입(클래스/레코드/구조체, 그리고 <see cref="TraceLensConfig.IncludeInterfaces"/>가 켜져 있으면 인터페이스)과
/// 그 멤버를 문서 단위로 한 번씩 훑는다.
/// 진입점에서 도달하지 못하는 클래스도 목록에 넣기 위해 항상 실행하고,
/// <see cref="AnalysisScope.Solution"/>일 때는 같은 순회에서 호출 엣지까지 모은다.
/// Minimal API 핸들러가 람다·로컬 함수인 경우에는 타입 멤버 순회로 잡히지 않으므로
/// <paramref name="extraCallables"/>로 받아 같은 규칙으로 노드·엣지를 만든다.
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
        IReadOnlyList<AnalysisCallable> extraCallables,
        CancellationToken cancellationToken = default)
    {
        var classes = new Dictionary<string, ClassBuilder>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var edges = new EdgeCollector();
        var pendingConstructors = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var memberStopwatch = new Stopwatch();
        var collector = new CallSiteCollector(config.IncludeCallSites, config.MaxCallSitesPerEdge, config.MaxBoundaryCallSites);
        var extraByDocument = extraCallables
            .GroupBy(item => item.Declaration.SyntaxTree.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        // 이미 자기 노드가 있는 람다(Minimal API 핸들러)는 부모 순회에서 건너뛴다. 같은 호출이 두 번 귀속되지 않게 한다.
        var registeredLambdas = extraCallables
            .Where(item => item.Kind == CallableKind.Lambda)
            .Select(item => LambdaKey.For(item.Declaration))
            .ToHashSet();

        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            var projectKey = project.AssemblyName;
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
                    var isInterface = typeSyntax is InterfaceDeclarationSyntax;
                    if (isInterface && !config.IncludeInterfaces)
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
                        var startLine = typeLineSpan.StartLinePosition.Line + 1;
                        var endLine = typeLineSpan.EndLinePosition.Line + 1;
                        builder = new ClassBuilder(
                            type,
                            classId,
                            nodeClassifier.Classify(type, config),
                            relativePath,
                            startLine,
                            endLine,
                            CollectMembers(type, config, startLine, endLine, memberStopwatch, cancellationToken));
                        classes.Add(classId, builder);
                    }

                    // 인터페이스 멤버는 본문이 없어 실행 그래프의 노드가 되지 못한다. members에만 담는다.
                    if (isInterface)
                    {
                        continue;
                    }

                    foreach (var callable in EnumerateCallables(typeSyntax, model, projectKey, cancellationToken))
                    {
                        var node = GraphNodes.Create(callable, solutionDirectory, builder.Kind);
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
                        AddCalls(model, callable, node.Id, solutionDirectory, config, nodes, edges, pendingConstructors, collector, registeredLambdas, cancellationToken);
                    }
                }

                if (!collectCalls || !extraByDocument.TryGetValue(document.FilePath ?? string.Empty, out var extras))
                {
                    continue;
                }

                // Minimal API 람다 핸들러: 타입 멤버가 아니므로 여기서 따로 노드를 만든다.
                foreach (var callable in extras)
                {
                    var kind = nodeClassifier.Classify(callable.ContainingType, config);
                    var node = GraphNodes.Create(callable, solutionDirectory, kind);
                    if (node is null)
                    {
                        continue;
                    }

                    nodes[node.Id] = node;
                    if (classes.TryGetValue(node.ClassId, out var owner))
                    {
                        owner.AddMethod(node.Id);
                    }

                    AddCalls(model, callable, node.Id, solutionDirectory, config, nodes, edges, pendingConstructors, collector, registeredLambdas, cancellationToken);
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

        // 멤버의 typeIds·baseTypeId·interfaceIds는 솔루션 안 타입만 남겨야 해서 클래스 목록이 다 모인 뒤에 확정한다.
        var knownClassIds = classes.Keys.ToHashSet(StringComparer.Ordinal);
        var built = classes.Values
            .Select(builder => builder.Build(knownClassIds))
            .OrderBy(item => item.Namespace, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        var memberCount = built.Sum(item => item.Members.Count);
        logger.LogInformation(
            "클래스 {ClassCount}개(인터페이스 {InterfaceCount}개)를 찾았고 멤버 {MemberCount}개를 {ElapsedMs}ms에 모았다.",
            built.Length,
            built.Count(item => item.TypeKind == ClassTypeKind.Interface),
            memberCount,
            memberStopwatch.ElapsedMilliseconds);

        var nodeIds = nodes.Keys.ToHashSet(StringComparer.Ordinal);
        return new SolutionScanResult(
            built,
            nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray(),
            edges.Build(nodes.Keys.ToHashSet(StringComparer.Ordinal)),
            memberCount,
            memberStopwatch.ElapsedMilliseconds,
            collector.Build(nodeIds),
            collector.Stats,
            collector.DroppedForLimit);
    }

    private void AddCalls(
        SemanticModel model,
        AnalysisCallable callable,
        string fromNodeId,
        string solutionDirectory,
        TraceLensConfig config,
        Dictionary<string, GraphNode> nodes,
        EdgeCollector edges,
        Dictionary<string, GraphNode> pendingConstructors,
        CallSiteCollector collector,
        IReadOnlySet<LambdaKey> registeredLambdas,
        CancellationToken cancellationToken)
    {
        foreach (var call in invocationResolver.ResolveCalls(
                     model, callable.Declaration, solutionDirectory, config, cancellationToken, registeredLambdas))
        {
            if (!call.IsEdge || call.Target is null)
            {
                collector.Add(fromNodeId, null, call);
                continue;
            }

            var targetId = CallableIds.ForMethod(call.Target);
            edges.Add(fromNodeId, targetId, call.Confidence);
            collector.Add(fromNodeId, targetId, call);
            QueueConstructorNode(call.Target, solutionDirectory, config, nodes, pendingConstructors, cancellationToken);
        }
    }

    /// <summary>멤버 수집이 분석 시간에서 차지하는 몫을 따로 재 둔다(설정으로 끌 수 있는 기능이라 비용을 알아야 한다).</summary>
    private static IReadOnlyList<ClassMemberCollector.MemberDraft> CollectMembers(
        INamedTypeSymbol type,
        TraceLensConfig config,
        int startLine,
        int endLine,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (!config.IncludeMembers)
        {
            return [];
        }

        stopwatch.Start();
        try
        {
            return ClassMemberCollector.Collect(type, startLine, endLine, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// 노드로 만들 callable을 돌려준다. 메서드·명시적 생성자·본문이 있는 프로퍼티에 더해
    /// 인덱서·연산자·이벤트 접근자·필드와 프로퍼티 초기화식도 대상이다.
    /// 그 안에 선언된 로컬 함수도 따로 낸다(로컬 함수는 별도 호출 대상이 될 수 있다).
    /// </summary>
    private static IEnumerable<AnalysisCallable> EnumerateCallables(
        TypeDeclarationSyntax typeSyntax,
        SemanticModel model,
        string projectKey,
        CancellationToken cancellationToken)
    {
        foreach (var member in typeSyntax.Members)
        {
            SyntaxNode? syntax = null;
            IMethodSymbol? symbol = null;
            switch (member)
            {
                case MethodDeclarationSyntax methodSyntax:
                    symbol = model.GetDeclaredSymbol(methodSyntax, cancellationToken) as IMethodSymbol;
                    syntax = methodSyntax;
                    break;

                case ConstructorDeclarationSyntax constructorSyntax:
                    symbol = model.GetDeclaredSymbol(constructorSyntax, cancellationToken) as IMethodSymbol;
                    syntax = constructorSyntax;
                    break;

                // 자동 프로퍼티는 실행할 코드가 없으므로 본문이나 초기화식이 있는 프로퍼티만 노드로 만든다.
                case PropertyDeclarationSyntax propertySyntax
                    when HasBody(propertySyntax) || propertySyntax.Initializer is not null:
                    var property = model.GetDeclaredSymbol(propertySyntax, cancellationToken) as IPropertySymbol;
                    symbol = property?.GetMethod ?? property?.SetMethod;
                    syntax = propertySyntax;
                    break;

                // 인덱서도 본문이 있는 프로퍼티와 같다.
                case IndexerDeclarationSyntax indexerSyntax:
                    var indexer = model.GetDeclaredSymbol(indexerSyntax, cancellationToken) as IPropertySymbol;
                    symbol = indexer?.GetMethod ?? indexer?.SetMethod;
                    syntax = indexerSyntax;
                    break;

                case OperatorDeclarationSyntax operatorSyntax:
                    symbol = model.GetDeclaredSymbol(operatorSyntax, cancellationToken) as IMethodSymbol;
                    syntax = operatorSyntax;
                    break;

                case ConversionOperatorDeclarationSyntax conversionSyntax:
                    symbol = model.GetDeclaredSymbol(conversionSyntax, cancellationToken) as IMethodSymbol;
                    syntax = conversionSyntax;
                    break;

                // 본문이 있는 이벤트 접근자(add/remove). 필드형 이벤트는 실행할 코드가 없다.
                case EventDeclarationSyntax eventSyntax:
                    var eventSymbol = model.GetDeclaredSymbol(eventSyntax, cancellationToken) as IEventSymbol;
                    symbol = eventSymbol?.AddMethod ?? eventSymbol?.RemoveMethod;
                    syntax = eventSyntax;
                    break;

                // 필드 초기화식은 생성자에서 실행된다. 여기서 부르는 것을 잃지 않도록 생성자 노드에 귀속한다.
                case FieldDeclarationSyntax fieldSyntax
                    when fieldSyntax.Declaration.Variables.Any(item => item.Initializer is not null):
                    symbol = FindInitializerOwner(model, typeSyntax, fieldSyntax, cancellationToken);
                    syntax = fieldSyntax;
                    break;
            }

            if (symbol is null || syntax is null)
            {
                continue;
            }

            yield return AnalysisCallable.ForMethod(symbol, syntax, projectKey);

            foreach (var localFunction in syntax.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                if (model.GetDeclaredSymbol(localFunction, cancellationToken) is IMethodSymbol local)
                {
                    yield return AnalysisCallable.ForLocalFunction(
                        local, localFunction, projectKey, CallableIds.ForMethod(symbol));
                }
            }
        }
    }

    /// <summary>
    /// 필드·프로퍼티 초기화식이 실제로 실행되는 자리는 생성자다.
    /// 명시적 생성자가 있으면 그 심볼을, 없으면 컴파일러가 만든 기본 생성자를 쓴다.
    /// </summary>
    private static IMethodSymbol? FindInitializerOwner(
        SemanticModel model,
        TypeDeclarationSyntax typeSyntax,
        SyntaxNode member,
        CancellationToken cancellationToken)
    {
        if (model.GetDeclaredSymbol(typeSyntax, cancellationToken) is not INamedTypeSymbol type)
        {
            return null;
        }

        var isStatic = member is FieldDeclarationSyntax { Modifiers: var modifiers } &&
                       modifiers.Any(item => item.IsKind(SyntaxKind.StaticKeyword));
        return isStatic
            ? type.StaticConstructors.FirstOrDefault()
            : type.InstanceConstructors.FirstOrDefault(item => !item.IsStatic);
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
        int endLine,
        IReadOnlyList<ClassMemberCollector.MemberDraft> memberDrafts)
    {
        private readonly SortedSet<string> methodIds = new(StringComparer.Ordinal);

        public NodeKind Kind { get; } = kind;

        public void AddMethod(string methodId) => methodIds.Add(methodId);

        public GraphClass Build(IReadOnlySet<string> knownClassIds)
        {
            var baseType = GetDeclaredBaseType(symbol);
            var interfaces = symbol.Interfaces
                .Select(item => (Id: SymbolNames.GetClassId(item.OriginalDefinition), Symbol: item))
                .ToArray();

            return new GraphClass(
                id,
                SymbolNames.GetClassDisplayName(symbol),
                symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
                Kind,
                file,
                startLine,
                endLine,
                methodIds.ToArray())
            {
                TypeKind = GetTypeKind(symbol),
                IsAbstract = symbol.IsAbstract && symbol.TypeKind != Microsoft.CodeAnalysis.TypeKind.Interface,
                IsStatic = symbol.IsStatic,
                BaseTypeId = baseType is not null && knownClassIds.Contains(SymbolNames.GetClassId(baseType.OriginalDefinition))
                    ? SymbolNames.GetClassId(baseType.OriginalDefinition)
                    : null,
                BaseTypeName = baseType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                InterfaceIds = interfaces.Where(item => knownClassIds.Contains(item.Id)).Select(item => item.Id).ToArray(),
                InterfaceNames = interfaces
                    .Select(item => item.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                    .ToArray(),
                Members = memberDrafts.Select(draft => draft.ToMember(knownClassIds, methodIds)).ToArray()
            };
        }

        /// <summary>소스에 적은 상위 타입만 돌려준다. <c>object</c>·<c>ValueType</c>처럼 컴파일러가 붙이는 것은 뺀다.</summary>
        private static INamedTypeSymbol? GetDeclaredBaseType(INamedTypeSymbol symbol) =>
            symbol.BaseType is { SpecialType: SpecialType.None } baseType ? baseType : null;

        private static ClassTypeKind GetTypeKind(INamedTypeSymbol symbol) => symbol switch
        {
            { TypeKind: Microsoft.CodeAnalysis.TypeKind.Interface } => ClassTypeKind.Interface,
            { IsRecord: true } => ClassTypeKind.Record,
            { TypeKind: Microsoft.CodeAnalysis.TypeKind.Struct } => ClassTypeKind.Struct,
            _ => ClassTypeKind.Class
        };
    }
}

/// <param name="MemberCount">수집한 멤버 수. 분석 응답에서 <c>graph.json</c>이 얼마나 커졌는지 가늠하는 데 쓴다.</param>
/// <param name="MemberElapsedMs">멤버 수집에만 쓴 시간(ms).</param>
public sealed record SolutionScanResult(
    IReadOnlyList<GraphClass> Classes,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    int MemberCount,
    long MemberElapsedMs,
    IReadOnlyList<CallSite> CallSites,
    CallResolutionStats Stats,
    int DroppedCallSites);

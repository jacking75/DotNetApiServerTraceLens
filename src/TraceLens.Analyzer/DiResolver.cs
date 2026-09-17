using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>인터페이스 호출을 구현체로 이었는지, 못 이었다면 왜 못 이었는지.</summary>
public sealed record DiResolution(
    IMethodSymbol? Method,
    CallResolution Resolution,
    CallConfidence Confidence,
    string Note)
{
    public static DiResolution Failed(string note) =>
        new(null, CallResolution.Ambiguous, CallConfidence.Estimated, note);
}

/// <summary>
/// 인터페이스 호출을 실제 구현체 메서드로 잇기 위한 정보를 모은다.
/// DI 등록(<c>AddScoped</c>/<c>AddSingleton</c>/<c>TryAddTransient</c> 등)을 먼저 보고,
/// 등록을 찾지 못하면 솔루션 안 구현체가 하나뿐일 때만 그 구현체로 잇는다.
/// 등록 메서드는 이름만 보지 않고 실제 심볼이 <c>Microsoft.Extensions.DependencyInjection</c>의
/// <c>IServiceCollection</c> 확장인지 확인한다. keyed 등록과 서로 다른 구현체의 중복 등록은 확정하지 않는다.
/// </summary>
public sealed class DiResolver(ILogger<DiResolver> logger)
{
    /// <summary>DI 등록 호출이 들어 있을 수 있는 파일만 고르기 위한 텍스트 힌트.</summary>
    private static readonly string[] RegistrationHints =
    [
        "AddScoped", "AddSingleton", "AddTransient", "TryAdd", "AddKeyed",
        "AddHttpClient", "AddHostedService", "typeof("
    ];

    private const string DiNamespace = "Microsoft.Extensions.DependencyInjection";

    /// <summary>인터페이스 키 → 찾은 DI 등록들.</summary>
    private readonly Dictionary<string, List<Registration>> registrations = new(StringComparer.Ordinal);

    /// <summary>인터페이스 키 → 솔루션 안에서 그 인터페이스를 구현한 클래스들.</summary>
    private readonly Dictionary<string, List<INamedTypeSymbol>> implementations = new(StringComparer.Ordinal);

    /// <summary>기반 클래스 키 → 솔루션 안에서 그 클래스를 상속한 클래스들. virtual/abstract 디스패치에 쓴다.</summary>
    private readonly Dictionary<string, List<INamedTypeSymbol>> derivedTypes = new(StringComparer.Ordinal);

    /// <param name="Conditional">if/switch/삼항 안에서 등록됐다. 실행 시 어느 쪽이 쓰일지 정적으로 확정할 수 없다.</param>
    private sealed record Registration(INamedTypeSymbol Implementation, bool Keyed, bool Conditional)
    {
        /// <summary>어떤 모양의 등록이었는지(자기 등록·팩토리·open generic). 근거 문구에 붙인다.</summary>
        public string Note { get; init; } = string.Empty;
    }

    public int RegistrationCount => registrations.Sum(entry => entry.Value.Count);

    public async Task BuildAsync(LoadedSolution loaded, CancellationToken cancellationToken = default)
    {
        registrations.Clear();
        implementations.Clear();
        derivedTypes.Clear();

        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is not null)
            {
                CollectImplementations(compilation, cancellationToken);
            }

            foreach (var document in project.Documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular))
            {
                if (!await HasRegistrationHintAsync(document, cancellationToken))
                {
                    continue;
                }

                await CollectRegistrationsAsync(document, cancellationToken);
            }
        }

        logger.LogInformation(
            "DI 등록 {RegistrationCount}건, 구현체가 있는 인터페이스 {InterfaceCount}종을 찾았다.",
            RegistrationCount,
            implementations.Count);
    }

    /// <summary>
    /// virtual·abstract 메서드 호출을 솔루션 안 재정의로 잇는다.
    /// 재정의가 하나뿐이면 추정으로 잇고, 여럿이면 확정하지 않는다.
    /// 기반 선언으로 가는 직접 엣지는 호출자가 그대로 유지한다(이 결과는 "추가" 엣지다).
    /// </summary>
    public DiResolution ResolveOverride(IMethodSymbol method)
    {
        if (!method.IsVirtual && !method.IsAbstract)
        {
            return DiResolution.Failed("virtual·abstract 메서드가 아니다.");
        }

        var key = GetTypeKey(method.ContainingType);
        if (!derivedTypes.TryGetValue(key, out var derived) || derived.Count == 0)
        {
            return DiResolution.Failed("솔루션 안에서 재정의를 찾지 못했다.");
        }

        var overrides = derived
            .Select(type => FindOverride(type, method))
            .Where(item => item is not null)
            .Select(item => item!)
            .DistinctBy(SymbolNames.GetNodeId, StringComparer.Ordinal)
            .ToArray();
        if (overrides.Length == 0)
        {
            return DiResolution.Failed("상속한 클래스는 있지만 이 메서드를 재정의한 곳이 없다.");
        }

        if (overrides.Length > 1)
        {
            return DiResolution.Failed(
                $"재정의가 {overrides.Length}곳이라 어느 쪽이 실행될지 확정할 수 없다: " +
                string.Join(", ", overrides.Take(3).Select(item => item.ContainingType.Name)));
        }

        return new DiResolution(
            overrides[0],
            CallResolution.SingleImplementation,
            CallConfidence.Estimated,
            $"솔루션 안 재정의가 {overrides[0].ContainingType.Name} 하나뿐이라 연결했다. 기반 선언으로 가는 호출도 그대로 남는다.");
    }

    private static IMethodSymbol? FindOverride(INamedTypeSymbol type, IMethodSymbol baseMethod) => type
        .GetMembers(baseMethod.Name)
        .OfType<IMethodSymbol>()
        .FirstOrDefault(candidate =>
            candidate.IsOverride && HasMatchingSignature(baseMethod, candidate));

    public DiResolution Resolve(IMethodSymbol interfaceMethod)
    {
        var interfaceType = interfaceMethod.ContainingType;
        if (interfaceType.DeclaringSyntaxReferences.Length == 0)
        {
            // 솔루션 밖(프레임워크/패키지) 인터페이스는 어차피 그래프 대상이 아니다.
            return new DiResolution(null, CallResolution.External, CallConfidence.Certain, "솔루션 밖 인터페이스다.");
        }

        var key = GetTypeKey(interfaceType);
        if (registrations.TryGetValue(key, out var found) && found.Count > 0)
        {
            var keyed = found.Where(item => item.Keyed).ToArray();
            var plain = found.Where(item => !item.Keyed).ToArray();
            var distinct = plain.Select(item => GetTypeKey(item.Implementation)).Distinct(StringComparer.Ordinal).ToArray();
            if (distinct.Length > 1)
            {
                return DiResolution.Failed($"DI 등록이 {distinct.Length}종이라 어느 구현체인지 확정할 수 없다: {string.Join(", ", distinct.Take(3))}");
            }

            if (distinct.Length == 1)
            {
                var registration = plain[0];
                var implementation = registration.Implementation;
                var resolvedMethod = FindImplementation(implementation, interfaceMethod);
                if (resolvedMethod is null)
                {
                    return DiResolution.Failed($"등록한 구현체 {implementation.Name}에서 구현 메서드를 찾지 못했다.");
                }

                var conditional = plain.Any(item => item.Conditional);
                var note = keyed.Length > 0
                    ? "keyed 등록이 함께 있어 실행 시 다른 구현체가 쓰일 수 있다."
                    : conditional
                        ? "조건부(if/switch) 등록이라 실행 시 다른 구현체가 쓰일 수 있다."
                        : $"DI 등록으로 {implementation.Name}에 연결했다.";
                return new DiResolution(
                    resolvedMethod,
                    CallResolution.DiRegistration,
                    conditional || keyed.Length > 0 ? CallConfidence.Estimated : CallConfidence.Certain,
                    note);
            }

            return DiResolution.Failed("keyed 등록만 있어 호출 지점의 키를 알기 전에는 구현체를 확정할 수 없다.");
        }

        implementations.TryGetValue(key, out var candidates);
        if (candidates is not { Count: 1 })
        {
            logger.LogDebug(
                "구현체를 특정하지 못해 인터페이스 호출을 제외한다: {Method} (후보 {Count}개)",
                interfaceMethod.ToDisplayString(),
                candidates?.Count ?? 0);
            return DiResolution.Failed(candidates is null || candidates.Count == 0
                ? "DI 등록도 솔루션 안 구현체도 찾지 못했다."
                : $"DI 등록이 없고 솔루션 안 구현체가 {candidates.Count}개라 확정할 수 없다.");
        }

        var single = FindImplementation(candidates[0], interfaceMethod);
        return single is null
            ? DiResolution.Failed($"유일한 구현체 {candidates[0].Name}에서 구현 메서드를 찾지 못했다.")
            : new DiResolution(
                single,
                CallResolution.SingleImplementation,
                CallConfidence.Estimated,
                $"DI 등록은 없지만 솔루션 안 구현체가 {candidates[0].Name} 하나뿐이라 연결했다.");
    }

    private static IMethodSymbol? FindImplementation(INamedTypeSymbol implementation, IMethodSymbol interfaceMethod) =>
        implementation.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol ??
        implementation.GetMembers(interfaceMethod.Name)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(candidate => HasMatchingSignature(interfaceMethod, candidate));

    private static async Task<bool> HasRegistrationHintAsync(Document document, CancellationToken cancellationToken)
    {
        var text = (await document.GetTextAsync(cancellationToken)).ToString();
        return RegistrationHints.Any(hint => text.Contains(hint, StringComparison.Ordinal));
    }

    private async Task CollectRegistrationsAsync(Document document, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var model = await document.GetSemanticModelAsync(cancellationToken);
        if (root is null || model is null)
        {
            return;
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = invocation.Expression.DescendantNodesAndSelf()
                .OfType<SimpleNameSyntax>()
                .FirstOrDefault(item => IsRegistrationName(item.Identifier.ValueText));
            if (name is null || !IsServiceCollectionExtension(model, invocation, cancellationToken))
            {
                continue;
            }

            var pair = ReadRegistrationPair(name, invocation, model, cancellationToken);
            if (pair is null)
            {
                continue;
            }

            var (abstraction, implementation, note) = pair.Value;
            var key = GetTypeKey(abstraction);
            if (!registrations.TryGetValue(key, out var list))
            {
                list = [];
                registrations[key] = list;
            }

            list.Add(new Registration(
                implementation,
                name.Identifier.ValueText.Contains("Keyed", StringComparison.Ordinal),
                IsConditional(invocation))
            {
                Note = note
            });
            logger.LogDebug("DI 등록을 찾았다: {Abstraction} -> {Implementation}", abstraction, implementation);
        }
    }

    /// <summary>
    /// 등록 한 줄에서 (추상, 구현) 짝을 읽는다. 지원하는 모양:
    /// <list type="bullet">
    ///   <item><c>AddScoped&lt;I, Impl&gt;()</c> — 제네릭 인자 두 개.</item>
    ///   <item><c>AddScoped&lt;Impl&gt;()</c> — 자기 등록(추상 = 구현).</item>
    ///   <item><c>AddScoped&lt;I&gt;(sp =&gt; new Impl(...))</c>·<c>sp.GetRequiredService&lt;Impl&gt;()</c> — 본문이 단일 생성/조회일 때만.</item>
    ///   <item><c>AddSingleton&lt;I&gt;(instance)</c> — 변수의 선언 타입으로 확정.</item>
    ///   <item><c>AddScoped(typeof(IRepo&lt;&gt;), typeof(Repo&lt;&gt;))</c> — open generic.</item>
    /// </list>
    /// 해석하지 못하는 팩토리 본문은 등록으로 잡지 않는다(추정으로 잇지 않는다).
    /// </summary>
    private static (INamedTypeSymbol Abstraction, INamedTypeSymbol Implementation, string Note)? ReadRegistrationPair(
        SimpleNameSyntax name,
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (name is GenericNameSyntax generic)
        {
            var typeArguments = generic.TypeArgumentList.Arguments;
            if (typeArguments.Count == 2)
            {
                var abstraction = model.GetTypeInfo(typeArguments[0], cancellationToken).Type as INamedTypeSymbol;
                var implementation = model.GetTypeInfo(typeArguments[1], cancellationToken).Type as INamedTypeSymbol;
                return abstraction is not null && implementation is not null && IsRegistrationPair(abstraction, implementation)
                    ? (abstraction, implementation, string.Empty)
                    : null;
            }

            if (typeArguments.Count != 1)
            {
                return null;
            }

            if (model.GetTypeInfo(typeArguments[0], cancellationToken).Type is not INamedTypeSymbol declared)
            {
                return null;
            }

            // AddScoped<Impl>() — 자기 등록. 인터페이스라면 구현을 알 수 없으므로 팩토리·인스턴스를 본다.
            if (arguments.Count == 0)
            {
                return declared.TypeKind == TypeKind.Interface ? null : (declared, declared, "자기 등록");
            }

            var fromArgument = ReadImplementationFromArgument(arguments[^1].Expression, model, cancellationToken);
            return fromArgument is not null && IsRegistrationPair(declared, fromArgument)
                ? (declared, fromArgument, "팩토리·인스턴스 등록")
                : null;
        }

        // AddScoped(typeof(IRepo<>), typeof(Repo<>)) — open generic은 선언 심볼로 잇는다.
        if (arguments.Count == 2 &&
            ReadTypeOf(arguments[0].Expression, model, cancellationToken) is { } openAbstraction &&
            ReadTypeOf(arguments[1].Expression, model, cancellationToken) is { } openImplementation &&
            IsRegistrationPair(openAbstraction, openImplementation))
        {
            return (openAbstraction, openImplementation, "open generic 등록");
        }

        return null;
    }

    /// <summary>팩토리 람다나 인스턴스 인자에서 구현 타입을 읽는다. 단일 <c>new</c>/<c>GetRequiredService</c>일 때만 확정한다.</summary>
    private static INamedTypeSymbol? ReadImplementationFromArgument(
        ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
    {
        if (expression is AnonymousFunctionExpressionSyntax lambda)
        {
            var body = lambda.Body;
            // 블록 본문은 여러 갈래가 있을 수 있어 확정하지 않는다.
            return body is ExpressionSyntax inner
                ? ReadImplementationFromArgument(inner, model, cancellationToken)
                : null;
        }

        if (expression is ObjectCreationExpressionSyntax creation)
        {
            return model.GetTypeInfo(creation, cancellationToken).Type as INamedTypeSymbol;
        }

        if (expression is InvocationExpressionSyntax call &&
            call.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax resolveName } &&
            resolveName.Identifier.ValueText is "GetRequiredService" or "GetService" &&
            resolveName.TypeArgumentList.Arguments.Count == 1)
        {
            return model.GetTypeInfo(resolveName.TypeArgumentList.Arguments[0], cancellationToken).Type as INamedTypeSymbol;
        }

        // 변수로 넘긴 인스턴스는 선언 타입으로 본다.
        var type = model.GetTypeInfo(expression, cancellationToken).Type as INamedTypeSymbol;
        return type is { TypeKind: not TypeKind.Interface } ? type : null;
    }

    private static INamedTypeSymbol? ReadTypeOf(
        ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken) =>
        expression is TypeOfExpressionSyntax typeOf
            ? model.GetTypeInfo(typeOf.Type, cancellationToken).Type?.OriginalDefinition as INamedTypeSymbol
            : null;

    /// <summary>
    /// 이름이 <c>Add*</c>라고 다 DI 등록은 아니다. 실제 심볼이
    /// <c>Microsoft.Extensions.DependencyInjection</c>의 <c>IServiceCollection</c> 확장인지 확인한다.
    /// </summary>
    private static bool IsServiceCollectionExtension(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var symbol = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol ??
                     model.GetSymbolInfo(invocation, cancellationToken).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (symbol is null)
        {
            return false;
        }

        var target = symbol.ReducedFrom ?? symbol;
        var namespaceName = target.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!namespaceName.Equals(DiNamespace, StringComparison.Ordinal) &&
            !namespaceName.StartsWith(DiNamespace + ".", StringComparison.Ordinal))
        {
            return false;
        }

        var receiverType = target.IsExtensionMethod && target.Parameters.Length > 0
            ? target.Parameters[0].Type
            : target.ReceiverType;
        return receiverType is not null &&
               (receiverType.Name == "IServiceCollection" ||
                receiverType.AllInterfaces.Any(item => item.Name == "IServiceCollection"));
    }

    /// <summary>if/switch/삼항 안에서 등록됐는지. 실행 시 어느 등록이 적용될지 정적으로 확정할 수 없다는 표시다.</summary>
    private static bool IsConditional(SyntaxNode invocation)
    {
        for (var current = invocation.Parent; current is not null; current = current.Parent)
        {
            if (current is IfStatementSyntax or SwitchStatementSyntax or SwitchExpressionSyntax or ConditionalExpressionSyntax)
            {
                return true;
            }

            if (current is MemberDeclarationSyntax or CompilationUnitSyntax)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary><c>AddScoped</c>/<c>AddSingleton</c>/<c>TryAddScoped</c>/<c>AddKeyedTransient</c> 같은 등록 메서드 이름인지 확인한다.</summary>
    private static bool IsRegistrationName(string name) =>
        name.StartsWith("Add", StringComparison.Ordinal) || name.StartsWith("TryAdd", StringComparison.Ordinal);

    /// <summary>추상 타입과 그 구현체 조합인지 확인해서 이름만 비슷한 다른 제네릭 메서드를 걸러낸다.</summary>
    private static bool IsRegistrationPair(INamedTypeSymbol abstraction, INamedTypeSymbol implementation)
    {
        if (abstraction.TypeKind != TypeKind.Interface && !abstraction.IsAbstract)
        {
            return false;
        }

        if (implementation.TypeKind != TypeKind.Class || implementation.IsAbstract)
        {
            return false;
        }

        var abstractionKey = GetTypeKey(abstraction);
        if (implementation.AllInterfaces.Any(item => GetTypeKey(item) == abstractionKey))
        {
            return true;
        }

        for (var baseType = implementation.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (GetTypeKey(baseType) == abstractionKey)
            {
                return true;
            }
        }

        return false;
    }

    private void CollectImplementations(Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var type in EnumerateNamedTypes(compilation.Assembly.GlobalNamespace, cancellationToken))
        {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic ||
                type.DeclaringSyntaxReferences.Length == 0)
            {
                continue;
            }

            var typeKey = GetTypeKey(type);
            foreach (var abstraction in type.AllInterfaces.Where(item => item.DeclaringSyntaxReferences.Length > 0))
            {
                var key = GetTypeKey(abstraction);
                if (!implementations.TryGetValue(key, out var candidates))
                {
                    candidates = [];
                    implementations[key] = candidates;
                }

                // 멀티 타깃 프로젝트에서 같은 클래스가 두 번 들어오지 않도록 키로 비교한다.
                if (!candidates.Any(candidate => GetTypeKey(candidate) == typeKey))
                {
                    candidates.Add(type);
                }
            }

            // virtual·abstract 디스패치를 위해 상속 관계도 모은다(솔루션 안 기반 클래스만).
            for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                if (baseType.DeclaringSyntaxReferences.Length == 0)
                {
                    continue;
                }

                var baseKey = GetTypeKey(baseType.OriginalDefinition);
                if (!derivedTypes.TryGetValue(baseKey, out var derived))
                {
                    derived = [];
                    derivedTypes[baseKey] = derived;
                }

                if (!derived.Any(candidate => GetTypeKey(candidate) == typeKey))
                {
                    derived.Add(type);
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(
        INamespaceSymbol namespaceSymbol,
        CancellationToken cancellationToken)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (member)
            {
                case INamespaceSymbol childNamespace:
                    foreach (var type in EnumerateNamedTypes(childNamespace, cancellationToken))
                    {
                        yield return type;
                    }

                    break;
                case INamedTypeSymbol namedType:
                    foreach (var type in EnumerateWithNestedTypes(namedType))
                    {
                        yield return type;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateWithNestedTypes(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var item in EnumerateWithNestedTypes(nested))
            {
                yield return item;
            }
        }
    }

    private static string GetTypeKey(ITypeSymbol symbol)
    {
        if (symbol is not INamedTypeSymbol namedType)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat).TrimEnd('?');
        }

        var original = namedType.OriginalDefinition;
        var typeNames = new Stack<string>();
        for (var current = original; current is not null; current = current.ContainingType)
        {
            typeNames.Push(current.MetadataName);
        }

        var namespaceName = original.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : original.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? string.Join('+', typeNames)
            : namespaceName + "." + string.Join('+', typeNames);
    }

    private static bool HasMatchingSignature(IMethodSymbol abstraction, IMethodSymbol implementation) =>
        abstraction.Parameters.Length == implementation.Parameters.Length &&
        abstraction.Parameters.Zip(implementation.Parameters)
            .All(pair => GetTypeKey(pair.First.Type) == GetTypeKey(pair.Second.Type));
}

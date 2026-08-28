using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace TraceLens.Analyzer;

/// <summary>
/// 인터페이스 호출을 실제 구현체 메서드로 잇기 위한 정보를 모은다.
/// DI 등록(<c>AddScoped</c>/<c>AddSingleton</c>/<c>TryAddTransient</c> 등)을 먼저 보고,
/// 등록을 찾지 못하면 솔루션 안 구현체가 하나뿐일 때만 그 구현체로 잇는다.
/// </summary>
public sealed class DiResolver(ILogger<DiResolver> logger)
{
    /// <summary>DI 등록 호출이 들어 있을 수 있는 파일만 고르기 위한 텍스트 힌트.</summary>
    private static readonly string[] RegistrationHints = ["AddScoped", "AddSingleton", "AddTransient", "TryAdd", "AddKeyed"];

    /// <summary>인터페이스 키 → DI로 등록된 구현체.</summary>
    private readonly Dictionary<string, INamedTypeSymbol> registrations = new(StringComparer.Ordinal);

    /// <summary>인터페이스 키 → 솔루션 안에서 그 인터페이스를 구현한 클래스들.</summary>
    private readonly Dictionary<string, List<INamedTypeSymbol>> implementations = new(StringComparer.Ordinal);

    public async Task BuildAsync(LoadedSolution loaded, CancellationToken cancellationToken = default)
    {
        registrations.Clear();
        implementations.Clear();

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
            registrations.Count,
            implementations.Count);
    }

    public IMethodSymbol? Resolve(IMethodSymbol interfaceMethod)
    {
        var interfaceType = interfaceMethod.ContainingType;
        if (interfaceType.DeclaringSyntaxReferences.Length == 0)
        {
            // 솔루션 밖(프레임워크/패키지) 인터페이스는 어차피 그래프 대상이 아니다.
            return null;
        }

        var key = GetTypeKey(interfaceType);
        if (!registrations.TryGetValue(key, out var implementation))
        {
            implementations.TryGetValue(key, out var candidates);
            if (candidates is not { Count: 1 })
            {
                logger.LogDebug(
                    "구현체를 특정하지 못해 인터페이스 호출을 제외한다: {Method} (후보 {Count}개)",
                    interfaceMethod.ToDisplayString(),
                    candidates?.Count ?? 0);
                return null;
            }

            implementation = candidates[0];
        }

        var resolved = implementation.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol ??
                       implementation.GetMembers(interfaceMethod.Name)
                           .OfType<IMethodSymbol>()
                           .FirstOrDefault(candidate => HasMatchingSignature(interfaceMethod, candidate));
        if (resolved is null)
        {
            logger.LogDebug("구현 메서드를 찾지 못해 인터페이스 호출을 제외한다: {Method}", interfaceMethod.ToDisplayString());
        }

        return resolved;
    }

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
            var genericName = invocation.Expression.DescendantNodesAndSelf()
                .OfType<GenericNameSyntax>()
                .FirstOrDefault(name =>
                    IsRegistrationName(name.Identifier.ValueText) && name.TypeArgumentList.Arguments.Count == 2);
            if (genericName is null)
            {
                continue;
            }

            var abstraction = model.GetTypeInfo(genericName.TypeArgumentList.Arguments[0], cancellationToken).Type as INamedTypeSymbol;
            var implementation = model.GetTypeInfo(genericName.TypeArgumentList.Arguments[1], cancellationToken).Type as INamedTypeSymbol;
            if (abstraction is null || implementation is null || !IsRegistrationPair(abstraction, implementation))
            {
                continue;
            }

            registrations[GetTypeKey(abstraction)] = implementation;
            logger.LogDebug("DI 등록을 찾았다: {Abstraction} -> {Implementation}", abstraction, implementation);
        }
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

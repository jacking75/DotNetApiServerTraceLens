using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace TraceLens.Analyzer;

public sealed class DiResolver(ILogger<DiResolver> logger)
{
    private readonly Dictionary<string, INamedTypeSymbol> mappings = new(StringComparer.Ordinal);

    public async Task BuildAsync(LoadedSolution loaded, CancellationToken cancellationToken = default)
    {
        mappings.Clear();
        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            foreach (var document in project.Documents.Where(document =>
                         string.Equals(Path.GetFileName(document.FilePath), "Program.cs", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Path.GetFileName(document.FilePath), "Startup.cs", StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogDebug("DI 등록 파일을 분석한다: {File}", document.FilePath);
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                var model = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || model is null)
                {
                    continue;
                }

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var genericName = invocation.Expression.DescendantNodesAndSelf()
                        .OfType<GenericNameSyntax>()
                        .FirstOrDefault(name => name.Identifier.ValueText == "AddScoped" && name.TypeArgumentList.Arguments.Count == 2);
                    if (genericName is null)
                    {
                        continue;
                    }

                    var abstraction = model.GetTypeInfo(genericName.TypeArgumentList.Arguments[0], cancellationToken).Type as INamedTypeSymbol;
                    var implementation = model.GetTypeInfo(genericName.TypeArgumentList.Arguments[1], cancellationToken).Type as INamedTypeSymbol;
                    if (abstraction is null || implementation is null)
                    {
                        logger.LogWarning("AddScoped 형식 인수를 해석하지 못했다: {Invocation}", invocation);
                        continue;
                    }

                    mappings[GetTypeKey(abstraction)] = implementation;
                    logger.LogDebug("DI 등록을 찾았다: {Abstraction} -> {Implementation}", abstraction, implementation);
                }
            }
        }
    }

    public IMethodSymbol? Resolve(IMethodSymbol interfaceMethod)
    {
        if (!mappings.TryGetValue(GetTypeKey(interfaceMethod.ContainingType), out var implementation))
        {
            logger.LogWarning("DI 등록을 찾지 못해 인터페이스 호출을 제외한다: {Method}", interfaceMethod.ToDisplayString());
            return null;
        }

        var resolved = implementation.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol ??
                       implementation.GetMembers(interfaceMethod.Name)
                           .OfType<IMethodSymbol>()
                           .FirstOrDefault(candidate => HasMatchingSignature(interfaceMethod, candidate));
        if (resolved is null)
        {
            logger.LogWarning("구현 메서드를 찾지 못해 인터페이스 호출을 제외한다: {Method}", interfaceMethod.ToDisplayString());
        }

        return resolved;
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

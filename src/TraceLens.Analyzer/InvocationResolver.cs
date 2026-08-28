using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace TraceLens.Analyzer;

/// <summary>
/// 메서드 본문 안의 호출식을 심볼로 해석하고, 인터페이스 호출은 DI 등록으로 구현체까지 연결한다.
/// <c>new X()</c>도 생성자(.ctor) 호출이므로 <see cref="TraceLensConfig.IncludeObjectCreation"/>이 켜져 있으면 함께 모은다.
/// </summary>
public sealed class InvocationResolver(DiResolver diResolver, ILogger<InvocationResolver> logger)
{
    public IReadOnlyList<IMethodSymbol> ResolveCalls(
        SemanticModel model,
        SyntaxNode methodSyntax,
        string solutionDirectory,
        TraceLensConfig config,
        CancellationToken cancellationToken = default)
    {
        var result = new List<IMethodSymbol>();
        foreach (var syntax in methodSyntax.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var calledMethod = syntax switch
            {
                InvocationExpressionSyntax invocation => GetCalledMethod(model, invocation, cancellationToken),
                BaseObjectCreationExpressionSyntax creation when config.IncludeObjectCreation =>
                    GetCalledMethod(model, creation, cancellationToken),
                _ => null
            };
            if (calledMethod is null)
            {
                continue;
            }

            var resolvedMethod = calledMethod.ContainingType.TypeKind == TypeKind.Interface
                ? diResolver.Resolve(calledMethod)
                : calledMethod;
            if (resolvedMethod is not null && IsSolutionMethod(resolvedMethod, solutionDirectory, config))
            {
                result.Add(resolvedMethod);
            }
        }

        return result;
    }

    private IMethodSymbol? GetCalledMethod(SemanticModel model, SyntaxNode syntax, CancellationToken cancellationToken)
    {
        var symbolInfo = model.GetSymbolInfo(syntax, cancellationToken);
        var calledMethod = symbolInfo.Symbol as IMethodSymbol ??
                           symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (calledMethod is null)
        {
            logger.LogDebug("호출 심볼을 해석하지 못했다: {Syntax}", syntax);
            return null;
        }

        return calledMethod.ReducedFrom ?? calledMethod;
    }

    public static bool IsSolutionMethod(IMethodSymbol method, string solutionDirectory, TraceLensConfig config)
    {
        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (config.ExcludeNamespacePrefixes.Any(prefix =>
                namespaceName.Equals(prefix, StringComparison.Ordinal) ||
                namespaceName.StartsWith(prefix + ".", StringComparison.Ordinal)))
        {
            return false;
        }

        return GetDeclarations(method).Any(reference =>
        {
            var relativePath = SourcePaths.ToSolutionRelative(solutionDirectory, reference.SyntaxTree.FilePath);
            return relativePath is not null && !SourcePaths.IsBuildOutput(relativePath);
        });
    }

    /// <summary>
    /// 선언 위치를 돌려준다. 컴파일러가 만들어 주는 기본 생성자에는 선언 구문이 없으므로 그 타입의 선언 위치로 대신한다.
    /// </summary>
    internal static IEnumerable<SyntaxReference> GetDeclarations(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Length > 0
            ? method.DeclaringSyntaxReferences
            : method.MethodKind == MethodKind.Constructor
                ? method.ContainingType.DeclaringSyntaxReferences
                : [];
}

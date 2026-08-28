using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace TraceLens.Analyzer;

/// <summary>메서드 본문 안의 호출식을 심볼로 해석하고, 인터페이스 호출은 DI 등록으로 구현체까지 연결한다.</summary>
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
        foreach (var invocation in methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbolInfo = model.GetSymbolInfo(invocation, cancellationToken);
            var calledMethod = symbolInfo.Symbol as IMethodSymbol ??
                               symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            if (calledMethod is null)
            {
                logger.LogDebug("호출 심볼을 해석하지 못했다: {Invocation}", invocation);
                continue;
            }

            calledMethod = calledMethod.ReducedFrom ?? calledMethod;
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

    public static bool IsSolutionMethod(IMethodSymbol method, string solutionDirectory, TraceLensConfig config)
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
            var relativePath = SourcePaths.ToSolutionRelative(solutionDirectory, reference.SyntaxTree.FilePath);
            return relativePath is not null && !SourcePaths.IsBuildOutput(relativePath);
        });
    }
}

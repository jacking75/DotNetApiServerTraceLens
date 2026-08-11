using Microsoft.CodeAnalysis;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

public sealed class NodeClassifier
{
    public NodeKind Classify(IMethodSymbol method, TraceLensConfig config)
    {
        foreach (var rule in config.KindRules)
        {
            var matchesBaseType = rule.MatchBaseType is not null && EnumerateBaseTypes(method.ContainingType)
                .Any(type => string.Equals(type.Name, rule.MatchBaseType, StringComparison.Ordinal) ||
                             type.ToDisplayString().EndsWith('.' + rule.MatchBaseType, StringComparison.Ordinal));
            var matchesSuffix = rule.MatchNameSuffix.Any(suffix =>
                method.ContainingType.Name.EndsWith(suffix, StringComparison.Ordinal));
            if (matchesBaseType || matchesSuffix)
            {
                return Enum.TryParse<NodeKind>(rule.Kind, true, out var kind) ? kind : NodeKind.Method;
            }
        }

        return NodeKind.Method;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateBaseTypes(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }
}

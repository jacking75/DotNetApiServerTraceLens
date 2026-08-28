using Microsoft.CodeAnalysis;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

public sealed class NodeClassifier
{
    public NodeKind Classify(IMethodSymbol method, TraceLensConfig config) =>
        Classify(method.ContainingType, config);

    public NodeKind Classify(INamedTypeSymbol type, TraceLensConfig config)
    {
        foreach (var rule in config.KindRules)
        {
            var matchesBaseType = rule.MatchBaseType is not null && EnumerateBaseTypes(type)
                .Any(candidate => string.Equals(candidate.Name, rule.MatchBaseType, StringComparison.Ordinal) ||
                                  candidate.ToDisplayString().EndsWith('.' + rule.MatchBaseType, StringComparison.Ordinal));
            var matchesSuffix = rule.MatchNameSuffix.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal));
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

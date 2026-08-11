using Microsoft.CodeAnalysis;

namespace TraceLens.Analyzer;

internal static class SymbolNames
{
    private static readonly SymbolDisplayFormat TypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string GetNodeId(IMethodSymbol method)
    {
        var containingType = method.ContainingType.ToDisplayString(TypeFormat);
        var parameters = string.Join(",", method.Parameters.Select(parameter => parameter.Type.ToDisplayString(TypeFormat)));
        return $"{containingType}.{method.Name}({parameters})";
    }

    public static string GetDisplayName(IMethodSymbol method) => $"{method.ContainingType.Name}.{method.Name}";
}

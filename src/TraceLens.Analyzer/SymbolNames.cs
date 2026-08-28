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
        var containingType = GetClassId(method.ContainingType);
        var parameters = string.Join(",", method.Parameters.Select(parameter => parameter.Type.ToDisplayString(TypeFormat)));
        return $"{containingType}.{method.Name}({parameters})";
    }

    public static string GetDisplayName(IMethodSymbol method) => $"{method.ContainingType.Name}.{method.Name}";

    public static string GetClassId(INamedTypeSymbol type) => type.ToDisplayString(TypeFormat);

    public static string GetClassDisplayName(INamedTypeSymbol type)
    {
        var names = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            names.Push(current.TypeParameters.Length == 0
                ? current.Name
                : $"{current.Name}<{string.Join(", ", current.TypeParameters.Select(parameter => parameter.Name))}>");
        }

        return string.Join('.', names);
    }
}

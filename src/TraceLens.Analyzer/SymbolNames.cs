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

    public static string GetDisplayName(IMethodSymbol method) => method.MethodKind switch
    {
        // 생성자와 프로퍼티 접근자는 심볼 이름(.ctor/get_X)을 그대로 쓰면 읽기 어려우므로 소스에 보이는 모양으로 바꾼다.
        MethodKind.Constructor => $"new {method.ContainingType.Name}",
        MethodKind.PropertyGet or MethodKind.PropertySet =>
            $"{method.ContainingType.Name}.{method.AssociatedSymbol?.Name ?? method.Name}",
        _ => $"{method.ContainingType.Name}.{method.Name}"
    };

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

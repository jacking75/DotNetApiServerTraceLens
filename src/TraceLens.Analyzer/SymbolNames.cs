using System.Text;
using Microsoft.CodeAnalysis;

namespace TraceLens.Analyzer;

internal static class SymbolNames
{
    private static readonly SymbolDisplayFormat TypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>안정 키에 쓰는 타입 이름. 표시용과 달리 nullable 표기를 빼서 <c>string?</c>과 <c>string</c>을 같게 본다.</summary>
    private static readonly SymbolDisplayFormat StableTypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static string GetNodeId(IMethodSymbol method)
    {
        var containingType = GetClassId(method.ContainingType);
        return $"{containingType}.{method.Name}({string.Join(",", GetParameterTypeNames(method))})";
    }

    /// <summary>노드 id에 쓰는 것과 같은 규칙(전체 타입 이름)으로 파라미터 타입을 돌려준다.</summary>
    public static IReadOnlyList<string> GetParameterTypeNames(IMethodSymbol method) =>
        method.Parameters.Select(parameter => parameter.Type.ToDisplayString(TypeFormat)).ToArray();

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

    /// <summary>
    /// 프로젝트·타입·메서드 종류·제네릭 인자 수·파라미터의 ref 종류와 타입까지 담은 비교용 키다.
    /// <see cref="GetNodeId"/>는 프로젝트가 다른 동명 타입과 일부 오버로드를 구분하지 못하므로 비교에는 이 키를 쓴다.
    /// </summary>
    public static string GetStableKey(IMethodSymbol method, string projectKey)
    {
        var builder = new StringBuilder();
        builder.Append(projectKey).Append("::");
        builder.Append(GetStableTypeKey(method.ContainingType)).Append("::");
        builder.Append(method.MethodKind).Append("::");
        builder.Append(method.Name);
        if (method.Arity > 0)
        {
            builder.Append('`').Append(method.Arity);
        }

        builder.Append('(');
        builder.AppendJoin(',', method.Parameters.Select(parameter =>
            (parameter.RefKind == RefKind.None ? string.Empty : parameter.RefKind.ToString().ToLowerInvariant() + " ") +
            parameter.Type.ToDisplayString(StableTypeFormat)));
        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>람다·로컬 함수처럼 심볼 이름이 안정적이지 않은 대상의 안정 키.</summary>
    public static string GetStableKey(string projectKey, string classId, string nodeId) =>
        $"{projectKey}::{classId}::Callable::{nodeId}";

    private static string GetStableTypeKey(INamedTypeSymbol type)
    {
        var original = type.OriginalDefinition;
        var names = new Stack<string>();
        for (var current = original; current is not null; current = current.ContainingType)
        {
            names.Push(current.MetadataName);
        }

        var namespaceName = original.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : original.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? string.Join('+', names)
            : namespaceName + "." + string.Join('+', names);
    }
}

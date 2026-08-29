using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 타입 심볼에서 클래스 다이어그램에 그릴 멤버를 뽑는다.
/// partial 클래스는 심볼 하나에 모든 멤버가 모이므로 여기서 자연히 병합되고,
/// 자동 프로퍼티 backing field 같은 컴파일러 생성 멤버는 <see cref="ISymbol.IsImplicitlyDeclared"/>로 걸러진다.
/// </summary>
internal static class ClassMemberCollector
{
    private static readonly SymbolDisplayFormat MemberTypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

    /// <summary>
    /// 멤버 초안. 솔루션 안 타입 id와 노드 id는 클래스·노드 목록이 다 모인 뒤에야 확정할 수 있어서
    /// 여기서는 후보만 담아 두고 <see cref="MemberDraft.ToMember"/>에서 걸러낸다.
    /// </summary>
    internal sealed record MemberDraft(
        MemberKind MemberKind,
        string Name,
        string? Signature,
        string Type,
        IReadOnlyList<string> CandidateTypeIds,
        MemberAccessibility Accessibility,
        bool IsStatic,
        int StartLine,
        int EndLine,
        string? CandidateNodeId)
    {
        public GraphClassMember ToMember(IReadOnlySet<string> knownClassIds, IReadOnlySet<string> nodeIds) => new(
            MemberKind,
            Name,
            Signature,
            Type,
            CandidateTypeIds.Where(knownClassIds.Contains).ToArray(),
            Accessibility,
            IsStatic,
            StartLine,
            EndLine,
            CandidateNodeId is not null && nodeIds.Contains(CandidateNodeId) ? CandidateNodeId : null);
    }

    public static IReadOnlyList<MemberDraft> Collect(
        INamedTypeSymbol type,
        int fallbackStartLine,
        int fallbackEndLine,
        CancellationToken cancellationToken)
    {
        var drafts = new List<MemberDraft>();
        foreach (var member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var draft = member switch
            {
                IFieldSymbol field when !field.IsImplicitlyDeclared =>
                    CreateField(field, fallbackStartLine, fallbackEndLine, cancellationToken),
                IPropertySymbol property when IsDeclaredInSource(property) =>
                    CreateProperty(property, fallbackStartLine, fallbackEndLine, cancellationToken),
                IMethodSymbol method when IsDiagramMethod(method) =>
                    CreateMethod(method, fallbackStartLine, fallbackEndLine, cancellationToken),
                _ => null
            };
            if (draft is not null)
            {
                drafts.Add(draft);
            }
        }

        // 선언 순서 대신 종류 → 줄 번호 순으로 정렬해야 다이어그램에서 읽기 좋다.
        return drafts
            .OrderBy(draft => (int)draft.MemberKind)
            .ThenBy(draft => draft.StartLine)
            .ThenBy(draft => draft.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>레코드의 <c>EqualityContract</c>처럼 소스에 대응하는 선언이 없는 멤버는 제외한다.</summary>
    private static bool IsDeclaredInSource(ISymbol symbol) =>
        !symbol.IsImplicitlyDeclared || symbol.DeclaringSyntaxReferences.Length > 0;

    /// <summary>
    /// 다이어그램에 그릴 메서드는 보통 메서드와 명시 선언 생성자뿐이다.
    /// 프로퍼티/이벤트 접근자, 연산자, 소멸자, 레코드가 자동 생성하는 멤버는 제외한다.
    /// </summary>
    private static bool IsDiagramMethod(IMethodSymbol method) =>
        !method.IsImplicitlyDeclared &&
        method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor;

    private static MemberDraft CreateField(
        IFieldSymbol field,
        int fallbackStartLine,
        int fallbackEndLine,
        CancellationToken cancellationToken)
    {
        var (start, end) = GetLines(field, fallbackStartLine, fallbackEndLine, cancellationToken);
        return new MemberDraft(
            MemberKind.Field,
            field.Name,
            null,
            field.Type.ToDisplayString(MemberTypeFormat),
            CollectTypeIds(field.Type),
            ToAccessibility(field.DeclaredAccessibility),
            field.IsStatic,
            start,
            end,
            null);
    }

    private static MemberDraft CreateProperty(
        IPropertySymbol property,
        int fallbackStartLine,
        int fallbackEndLine,
        CancellationToken cancellationToken)
    {
        var (start, end) = GetLines(property, fallbackStartLine, fallbackEndLine, cancellationToken);
        var accessor = property.GetMethod ?? property.SetMethod;
        return new MemberDraft(
            MemberKind.Property,
            property.IsIndexer ? "this[]" : property.Name,
            null,
            property.Type.ToDisplayString(MemberTypeFormat),
            CollectTypeIds(property.Type),
            ToAccessibility(property.DeclaredAccessibility),
            property.IsStatic,
            start,
            end,
            accessor is null ? null : SymbolNames.GetNodeId(accessor));
    }

    private static MemberDraft CreateMethod(
        IMethodSymbol method,
        int fallbackStartLine,
        int fallbackEndLine,
        CancellationToken cancellationToken)
    {
        var (start, end) = GetLines(method, fallbackStartLine, fallbackEndLine, cancellationToken);
        var isConstructor = method.MethodKind == MethodKind.Constructor;
        var name = isConstructor ? method.ContainingType.Name : method.Name;
        var parameters = string.Join(
            ", ",
            method.Parameters.Select(parameter => parameter.Type.ToDisplayString(MemberTypeFormat)));

        return new MemberDraft(
            isConstructor ? MemberKind.Constructor : MemberKind.Method,
            name,
            $"{name}({parameters})",
            isConstructor ? string.Empty : method.ReturnType.ToDisplayString(MemberTypeFormat),
            isConstructor ? [] : CollectTypeIds(method.ReturnType),
            ToAccessibility(method.DeclaredAccessibility),
            method.IsStatic,
            start,
            end,
            SymbolNames.GetNodeId(method));
    }

    public static MemberAccessibility ToAccessibility(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => MemberAccessibility.Public,
        Accessibility.Protected or Accessibility.ProtectedOrInternal or Accessibility.ProtectedAndInternal =>
            MemberAccessibility.Protected,
        Accessibility.Internal => MemberAccessibility.Internal,
        Accessibility.Private => MemberAccessibility.Private,
        // 인터페이스 멤버는 접근제한자를 적지 않아 NotApplicable로 온다.
        _ => MemberAccessibility.Public
    };

    /// <summary>제네릭 인자·배열 원소까지 재귀로 훑어 솔루션 안에 선언된 타입 id만 뽑는다.</summary>
    public static IReadOnlyList<string> CollectTypeIds(ITypeSymbol? type)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        AddTypeIds(type, ids);
        return ids.ToArray();
    }

    private static void AddTypeIds(ITypeSymbol? type, ISet<string> into)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                AddTypeIds(array.ElementType, into);
                return;

            case IPointerTypeSymbol pointer:
                AddTypeIds(pointer.PointedAtType, into);
                return;

            case INamedTypeSymbol named:
                var definition = named.OriginalDefinition;
                if (definition.TypeKind != Microsoft.CodeAnalysis.TypeKind.Error &&
                    definition.Locations.Any(location => location.IsInSource))
                {
                    into.Add(SymbolNames.GetClassId(definition));
                }

                foreach (var argument in named.TypeArguments)
                {
                    AddTypeIds(argument, into);
                }

                return;
        }
    }

    /// <summary>
    /// 선언 줄 번호. 선언 구문이 없으면 타입 선언 위치로 대신하고,
    /// 주 생성자는 선언 위치가 타입 전체라서 파라미터 목록만 가리키게 좁힌다.
    /// </summary>
    private static (int Start, int End) GetLines(
        ISymbol symbol,
        int fallbackStartLine,
        int fallbackEndLine,
        CancellationToken cancellationToken)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference is null)
        {
            return (fallbackStartLine, fallbackEndLine);
        }

        var syntax = reference.GetSyntax(cancellationToken);
        var location = syntax is TypeDeclarationSyntax typeSyntax
            ? typeSyntax.ParameterList?.GetLocation() ?? typeSyntax.Identifier.GetLocation()
            : syntax.GetLocation();
        var lineSpan = location.GetLineSpan();
        return (lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1);
    }
}

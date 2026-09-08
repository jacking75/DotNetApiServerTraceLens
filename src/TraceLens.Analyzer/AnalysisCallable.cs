using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 분석이 "본문을 훑는 단위"다. 메서드·생성자·프로퍼티 접근자뿐 아니라
/// Minimal API 핸들러로 쓰이는 람다와 로컬 함수도 같은 자리에 놓기 위해 만든 표현이다.
/// <see cref="ScannedEntryPoint"/>가 <see cref="IMethodSymbol"/>만 전제하던 구조를 이 타입으로 바꿨다.
/// </summary>
public sealed class AnalysisCallable
{
    public required string NodeId { get; init; }

    public required CallableKind Kind { get; init; }

    /// <summary>람다도 Roslyn에서는 익명 함수 심볼을 가지므로 항상 채워진다.</summary>
    public required IMethodSymbol Symbol { get; init; }

    /// <summary>위치와 본문 지문을 뽑는 선언 구문. 기본 생성자처럼 선언이 없으면 타입 선언이 들어온다.</summary>
    public required SyntaxNode Declaration { get; init; }

    public required INamedTypeSymbol ContainingType { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>어셈블리 이름. 안정 키의 앞부분으로 쓴다.</summary>
    public required string ProjectKey { get; init; }

    /// <summary>람다·로컬 함수를 감싼 callable의 노드 ID. 최상위 메서드면 null이다.</summary>
    public string? ParentNodeId { get; init; }

    public static AnalysisCallable ForMethod(IMethodSymbol method, SyntaxNode declaration, string projectKey) => new()
    {
        NodeId = CallableIds.ForMethod(method),
        Kind = ToKind(method),
        Symbol = method,
        Declaration = declaration,
        ContainingType = method.ContainingType,
        DisplayName = SymbolNames.GetDisplayName(method),
        ProjectKey = projectKey
    };

    public static AnalysisCallable ForLambda(
        IMethodSymbol lambdaSymbol,
        SyntaxNode lambdaSyntax,
        string projectKey,
        string displayName,
        string? parentNodeId) => new()
    {
        NodeId = CallableIds.ForLambda(lambdaSyntax, lambdaSymbol),
        Kind = CallableKind.Lambda,
        Symbol = lambdaSymbol,
        Declaration = lambdaSyntax,
        ContainingType = lambdaSymbol.ContainingType,
        DisplayName = displayName,
        ProjectKey = projectKey,
        ParentNodeId = parentNodeId
    };

    public static AnalysisCallable ForLocalFunction(
        IMethodSymbol localFunction,
        SyntaxNode declaration,
        string projectKey,
        string? parentNodeId) => new()
    {
        NodeId = CallableIds.ForLocalFunction(localFunction),
        Kind = CallableKind.LocalFunction,
        Symbol = localFunction,
        Declaration = declaration,
        ContainingType = localFunction.ContainingType,
        DisplayName = $"{localFunction.ContainingType.Name}.{localFunction.Name}",
        ProjectKey = projectKey,
        ParentNodeId = parentNodeId
    };

    public string StableKey => Kind is CallableKind.Lambda or CallableKind.LocalFunction
        ? SymbolNames.GetStableKey(ProjectKey, SymbolNames.GetClassId(ContainingType), NodeId)
        : SymbolNames.GetStableKey(Symbol, ProjectKey);

    private static CallableKind ToKind(IMethodSymbol method) => method.MethodKind switch
    {
        MethodKind.Constructor or MethodKind.StaticConstructor => CallableKind.Constructor,
        MethodKind.PropertyGet or MethodKind.PropertySet => CallableKind.PropertyAccessor,
        MethodKind.LocalFunction => CallableKind.LocalFunction,
        MethodKind.AnonymousFunction => CallableKind.Lambda,
        _ => CallableKind.Method
    };
}

/// <summary>
/// callable의 본문을 훑을 때 중첩된 callable(람다·로컬 함수) 안으로 들어가지 않도록 경계를 지키는 순회다.
/// 부모 메서드의 <c>DescendantNodes()</c>를 그대로 쓰면, 실제로 실행되지 않은 중첩 본문의 호출까지
/// 부모의 직접 호출로 잡히기 때문이다.
/// </summary>
internal static class CallableBodies
{
    public static IEnumerable<SyntaxNode> EnumerateOwnNodes(SyntaxNode declaration)
    {
        var stack = new Stack<SyntaxNode>();
        PushChildren(declaration);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (IsBoundary(node))
            {
                continue;
            }

            yield return node;
            PushChildren(node);
        }

        void PushChildren(SyntaxNode parent)
        {
            foreach (var child in parent.ChildNodes())
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>여기서부터는 다른 callable의 본문이다.</summary>
    public static bool IsBoundary(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;
}

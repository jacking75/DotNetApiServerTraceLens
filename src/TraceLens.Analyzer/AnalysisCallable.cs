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

/// <summary>이미 별도 callable로 등록된 람다(Minimal API 핸들러 등)를 가리키는 키.</summary>
public readonly record struct LambdaKey(string File, int SpanStart)
{
    public static LambdaKey For(SyntaxNode node) => new(node.SyntaxTree.FilePath ?? string.Empty, node.SpanStart);
}

/// <param name="Lambda">이 노드를 감싼 가장 바깥 람다. 람다 밖이면 null이다.</param>
internal readonly record struct BodyNode(SyntaxNode Node, AnonymousFunctionExpressionSyntax? Lambda);

/// <summary>
/// callable의 본문을 훑는 순회다. 로컬 함수는 자기 노드가 따로 있으므로 경계에서 멈춘다.
/// 람다는 <b>순회하되</b> "어떤 람다 안에 있는지"를 함께 넘긴다. 람다 본문의 호출을 아예 버리면
/// 트랜잭션 래퍼(<c>TransactionAsync(async (db, tx) =&gt; …)</c>)에 넘긴 호출이 그래프에도 경계 목록에도 남지 않아
/// 호출자가 없는 것처럼 보이기 때문이다. 대신 확실도를 낮추고 근거에 "람다 본문"을 남긴다.
/// 이미 별도 callable로 등록된 람다(Minimal API 핸들러)는 중복 귀속을 막으려고 건너뛴다.
/// </summary>
internal static class CallableBodies
{
    public static IEnumerable<BodyNode> EnumerateOwnNodes(
        SyntaxNode declaration,
        IReadOnlySet<LambdaKey>? excludedLambdas = null)
    {
        var stack = new Stack<BodyNode>();
        PushChildren(declaration, null);
        while (stack.Count > 0)
        {
            var (node, lambda) = stack.Pop();
            if (node is LocalFunctionStatementSyntax)
            {
                continue;
            }

            if (node is AnonymousFunctionExpressionSyntax anonymous)
            {
                if (excludedLambdas is not null && excludedLambdas.Contains(LambdaKey.For(anonymous)))
                {
                    continue;
                }

                // 람다 식 자체는 호출이 아니다. 본문만 "이 람다 경유"로 표시해 순회한다.
                PushChildren(anonymous, lambda ?? anonymous);
                continue;
            }

            yield return new BodyNode(node, lambda);
            PushChildren(node, lambda);
        }

        void PushChildren(SyntaxNode parent, AnonymousFunctionExpressionSyntax? lambda)
        {
            foreach (var child in parent.ChildNodes())
            {
                stack.Push(new BodyNode(child, lambda));
            }
        }
    }

    /// <summary>여기서부터는 다른 callable의 본문이다(자기 노드가 따로 생긴다).</summary>
    public static bool IsBoundary(SyntaxNode node) => node is LocalFunctionStatementSyntax;
}

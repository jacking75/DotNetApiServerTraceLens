using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TraceLens.Analyzer;

/// <summary>
/// 실행 본문을 가진 단위(메서드·생성자·로컬 함수·람다)의 노드 ID를 만든다.
/// 메서드는 기존 <see cref="SymbolNames.GetNodeId"/>를 그대로 쓰고,
/// 이름이 안정적이지 않은 로컬 함수·람다만 <b>구조 위치</b>(선언 순서)로 ID를 만든다.
/// 파일 줄 번호는 쓰지 않는다. 위아래로 코드가 밀려도 같은 ID가 유지되어야 하기 때문이다.
/// </summary>
internal static class CallableIds
{
    public static string ForMethod(IMethodSymbol method) => method.MethodKind switch
    {
        MethodKind.LocalFunction => ForLocalFunction(method),
        MethodKind.AnonymousFunction => ForLambdaSymbol(method),
        _ => SymbolNames.GetNodeId(method)
    };

    /// <summary><c>Ns.Class.Handle(int)#local(Validate)</c> 형태. 중첩 로컬 함수는 점으로 이어 붙인다.</summary>
    public static string ForLocalFunction(IMethodSymbol method)
    {
        var chain = new Stack<string>();
        ISymbol? current = method;
        while (current is IMethodSymbol { MethodKind: MethodKind.LocalFunction } local)
        {
            chain.Push(local.Name);
            current = local.ContainingSymbol;
        }

        return $"{SymbolNames.GetClassId(method.ContainingType)}.{DescribeOwner(current as IMethodSymbol)}#local({string.Join('.', chain)})";
    }

    /// <summary>람다 심볼에서 선언 구문을 찾아 <see cref="ForLambda"/>로 넘긴다.</summary>
    public static string ForLambdaSymbol(IMethodSymbol method)
    {
        var syntax = method.DeclaringSyntaxReferences.Length > 0
            ? method.DeclaringSyntaxReferences[0].GetSyntax()
            : null;
        return syntax is null
            ? $"{SymbolNames.GetClassId(method.ContainingType)}.{DescribeOwner(method.ContainingSymbol as IMethodSymbol)}#lambda[?]"
            : ForLambda(syntax, method);
    }

    /// <summary><c>Program.&lt;Main&gt;$(string[])#lambda[3]</c> 형태. 순번은 감싸는 멤버 안 람다의 소스 순서다.</summary>
    public static string ForLambda(SyntaxNode lambdaSyntax, IMethodSymbol lambdaSymbol)
    {
        var scope = FindLambdaScope(lambdaSyntax);
        var ordinal = 0;
        if (scope is not null)
        {
            foreach (var candidate in scope.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>())
            {
                if (candidate == lambdaSyntax)
                {
                    break;
                }

                ordinal++;
            }
        }

        var owner = DescribeOwner(lambdaSymbol.ContainingSymbol as IMethodSymbol);
        return $"{SymbolNames.GetClassId(lambdaSymbol.ContainingType)}.{owner}#lambda[{ordinal}]";
    }

    /// <summary>람다 순번을 셀 범위. 멤버 선언이 있으면 그 선언, 최상위 구문이면 파일 전체다.</summary>
    private static SyntaxNode? FindLambdaScope(SyntaxNode lambdaSyntax)
    {
        for (var current = lambdaSyntax.Parent; current is not null; current = current.Parent)
        {
            if (current is CompilationUnitSyntax || (current is MemberDeclarationSyntax and not GlobalStatementSyntax))
            {
                return current;
            }
        }

        return null;
    }

    private static string DescribeOwner(IMethodSymbol? owner) => owner is null
        ? "<top>"
        : $"{owner.Name}({string.Join(",", SymbolNames.GetParameterTypeNames(owner))})";
}

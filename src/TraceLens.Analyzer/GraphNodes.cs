using Microsoft.CodeAnalysis;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

internal static class GraphNodes
{
    /// <summary>
    /// 심볼만 있을 때 선언 위치를 찾아 노드를 만든다.
    /// 컴파일러가 만들어 주는 기본 생성자처럼 선언 구문이 없으면 그 타입 선언 위치를 쓴다.
    /// </summary>
    public static GraphNode? CreateFromSymbol(
        IMethodSymbol method,
        string solutionDirectory,
        NodeKind kind,
        CancellationToken cancellationToken = default)
    {
        var reference = InvocationResolver.GetDeclarations(method).FirstOrDefault();
        return reference is null
            ? null
            : Create(method, reference.GetSyntax(cancellationToken), solutionDirectory, kind);
    }

    /// <summary>메서드 심볼과 그 선언 구문으로 그래프 노드를 만든다. 솔루션 밖이거나 빌드 산출물이면 null이다.</summary>
    public static GraphNode? Create(IMethodSymbol method, SyntaxNode syntax, string solutionDirectory, NodeKind kind)
    {
        var relativePath = SourcePaths.ToSolutionRelative(solutionDirectory, syntax.SyntaxTree.FilePath);
        if (relativePath is null || SourcePaths.IsBuildOutput(relativePath))
        {
            return null;
        }

        var lineSpan = syntax.GetLocation().GetLineSpan();
        return new GraphNode(
            SymbolNames.GetNodeId(method),
            kind,
            SymbolNames.GetDisplayName(method),
            method.ContainingNamespace.IsGlobalNamespace ? string.Empty : method.ContainingNamespace.ToDisplayString(),
            SymbolNames.GetClassId(method.ContainingType),
            relativePath,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.EndLinePosition.Line + 1)
        {
            Accessibility = ClassMemberCollector.ToAccessibility(method.DeclaredAccessibility),
            // 생성자는 반환 타입이 void로 잡히지만 화면에는 아무것도 안 적는 편이 읽기 좋다.
            ReturnType = method.MethodKind == MethodKind.Constructor
                ? string.Empty
                : method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            ParameterTypes = SymbolNames.GetParameterTypeNames(method)
        };
    }
}

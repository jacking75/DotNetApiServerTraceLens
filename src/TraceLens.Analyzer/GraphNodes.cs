using Microsoft.CodeAnalysis;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

internal static class GraphNodes
{
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
            lineSpan.EndLinePosition.Line + 1);
    }
}

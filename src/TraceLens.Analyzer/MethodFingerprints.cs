using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TraceLens.Analyzer;

/// <summary>
/// 스냅샷 비교에서 "무엇이 바뀌었는지"를 파일 단위보다 좁게 보기 위한 지문이다.
/// 본문 지문은 trivia(공백·주석)를 뺀 토큰 종류와 텍스트의 열을 해시하므로 서식만 바꾼 변경과 구분된다.
/// 다만 이 값이 같다고 의미가 같다는 증명은 아니다(같은 동작을 다르게 쓴 코드는 다른 지문이 된다).
/// </summary>
internal static class MethodFingerprints
{
    /// <summary>토큰 사이 구분자. 토큰 텍스트가 우연히 이어 붙어 같은 지문이 되는 것을 막는다.</summary>
    private const char TokenSeparator = '\u001f';

    public static string ComputeBody(SyntaxNode? declaration)
    {
        var body = GetBody(declaration);
        if (body is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var token in body.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.EndOfFileToken))
            {
                continue;
            }

            builder.Append((int)token.RawKind).Append(':').Append(token.ValueText).Append(TokenSeparator);
        }

        return Hash(builder.ToString());
    }

    public static string ComputeSignature(IMethodSymbol method)
    {
        var builder = new StringBuilder();
        builder.Append(method.DeclaredAccessibility).Append(' ');
        builder.Append(method.IsStatic ? "static " : string.Empty);
        builder.Append(method.IsAbstract ? "abstract " : string.Empty);
        builder.Append(method.IsVirtual ? "virtual " : string.Empty);
        builder.Append(method.IsOverride ? "override " : string.Empty);
        builder.Append(method.IsAsync ? "async " : string.Empty);
        builder.Append(method.ReturnType.ToDisplayString()).Append(' ');
        builder.Append(method.Name);
        if (method.Arity > 0)
        {
            builder.Append('`').Append(method.Arity);
        }

        builder.Append('(');
        builder.AppendJoin(',', method.Parameters.Select(parameter =>
            $"{parameter.RefKind}:{parameter.Type.ToDisplayString()}:{(parameter.HasExplicitDefaultValue ? "=" : string.Empty)}"));
        builder.Append(')');
        return Hash(builder.ToString());
    }

    /// <summary>본문이 되는 구문을 돌려준다. 본문이 없는 선언(추상 메서드·인터페이스 멤버)은 null이다.</summary>
    private static SyntaxNode? GetBody(SyntaxNode? declaration) => declaration switch
    {
        null => null,
        MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
        ConstructorDeclarationSyntax constructor => (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody,
        LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody,
        PropertyDeclarationSyntax property => (SyntaxNode?)property.ExpressionBody ?? property.AccessorList,
        AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
        ParenthesizedLambdaExpressionSyntax lambda => (SyntaxNode?)lambda.Block ?? lambda.ExpressionBody,
        SimpleLambdaExpressionSyntax lambda => (SyntaxNode?)lambda.Block ?? lambda.ExpressionBody,
        AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
        // 주 생성자는 타입 선언 자체가 위치라서 본문 지문을 만들지 않는다(클래스 전체를 본문으로 보면 과하다).
        TypeDeclarationSyntax => null,
        _ => null
    };

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];
}

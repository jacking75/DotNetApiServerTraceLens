using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 호출 한 지점의 해석 결과다. 확정하지 못한 경우에도 근거를 남기려고 심볼 목록 대신 이 타입을 돌려준다.
/// </summary>
/// <param name="Target">해석한 대상. 확정하지 못했으면 null이고 엣지를 만들지 않는다.</param>
public sealed record ResolvedCall(
    IMethodSymbol? Target,
    string DeclaredTarget,
    CallResolution Resolution,
    CallConfidence Confidence,
    string File,
    int Line,
    string Note)
{
    public bool IsEdge => Target is not null &&
                          Resolution is CallResolution.Direct or CallResolution.DiRegistration or CallResolution.SingleImplementation;
}

/// <summary>
/// 메서드 본문 안의 호출식을 심볼로 해석하고, 인터페이스 호출은 DI 등록으로 구현체까지 연결한다.
/// <c>new X()</c>도 생성자(.ctor) 호출이므로 <see cref="TraceLensConfig.IncludeObjectCreation"/>이 켜져 있으면 함께 모은다.
/// 중첩된 람다·로컬 함수 본문은 <see cref="CallableBodies"/>가 경계에서 멈추므로 부모의 직접 호출로 잡히지 않는다.
/// </summary>
public sealed class InvocationResolver(DiResolver diResolver, ILogger<InvocationResolver> logger)
{
    public IReadOnlyList<ResolvedCall> ResolveCalls(
        SemanticModel model,
        SyntaxNode methodSyntax,
        string solutionDirectory,
        TraceLensConfig config,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ResolvedCall>();
        foreach (var syntax in CallableBodies.EnumerateOwnNodes(methodSyntax))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isCall = syntax switch
            {
                InvocationExpressionSyntax => true,
                BaseObjectCreationExpressionSyntax => config.IncludeObjectCreation,
                _ => false
            };
            if (!isCall)
            {
                continue;
            }

            result.Add(Resolve(model, syntax, solutionDirectory, config, cancellationToken));
        }

        return result;
    }

    private ResolvedCall Resolve(
        SemanticModel model,
        SyntaxNode syntax,
        string solutionDirectory,
        TraceLensConfig config,
        CancellationToken cancellationToken)
    {
        var lineSpan = syntax.GetLocation().GetLineSpan();
        var file = SourcePaths.ToSolutionRelative(solutionDirectory, syntax.SyntaxTree.FilePath) ?? string.Empty;
        var line = lineSpan.StartLinePosition.Line + 1;

        var symbolInfo = model.GetSymbolInfo(syntax, cancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol declared)
        {
            var candidates = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
            if (candidates.Length == 0)
            {
                logger.LogDebug("호출 심볼을 해석하지 못했다: {Syntax}", syntax);
                return new ResolvedCall(
                    null, Describe(syntax), CallResolution.Unresolved, CallConfidence.Estimated, file, line,
                    "심볼을 해석하지 못했다(컴파일 오류이거나 지원하지 않는 구문일 수 있다).");
            }

            // 이전에는 첫 후보를 그대로 확정 호출로 채택했다. 후보가 여러 개면 어느 쪽인지 증명할 수 없으므로 엣지를 만들지 않는다.
            return new ResolvedCall(
                null,
                candidates[0].ToDisplayString(),
                CallResolution.Ambiguous,
                CallConfidence.Estimated,
                file,
                line,
                $"후보 {candidates.Length}개 중 하나로 확정하지 못했다: {string.Join(", ", candidates.Take(3).Select(item => item.ToDisplayString()))}");
        }

        var calledMethod = declared.ReducedFrom ?? declared;
        var declaredTarget = calledMethod.ToDisplayString();

        if (calledMethod.ContainingType.TypeKind == TypeKind.Interface)
        {
            var di = diResolver.Resolve(calledMethod);
            if (di.Method is null)
            {
                return IsExcluded(calledMethod, config) || !IsDeclaredInSolution(calledMethod, solutionDirectory)
                    ? new ResolvedCall(null, declaredTarget, CallResolution.External, CallConfidence.Certain, file, line, di.Note)
                    : new ResolvedCall(null, declaredTarget, CallResolution.Ambiguous, CallConfidence.Estimated, file, line, di.Note);
            }

            return IsSolutionMethod(di.Method, solutionDirectory, config)
                ? new ResolvedCall(di.Method, declaredTarget, di.Resolution, di.Confidence, file, line, di.Note)
                : new ResolvedCall(null, declaredTarget, CallResolution.External, CallConfidence.Certain, file, line,
                    "구현체가 솔루션 밖에 있다.");
        }

        if (!IsSolutionMethod(calledMethod, solutionDirectory, config))
        {
            return new ResolvedCall(
                null, declaredTarget, CallResolution.External, CallConfidence.Certain, file, line,
                IsExcluded(calledMethod, config) ? "excludeNamespacePrefixes로 제외한 범위다." : "솔루션 밖 호출이다.");
        }

        return new ResolvedCall(
            calledMethod, declaredTarget, CallResolution.Direct, CallConfidence.Certain, file, line, string.Empty);
    }

    private static string Describe(SyntaxNode syntax)
    {
        var text = syntax.ToString();
        return text.Length <= 80 ? text : text[..80] + "…";
    }

    private static bool IsExcluded(IMethodSymbol method, TraceLensConfig config)
    {
        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return config.ExcludeNamespacePrefixes.Any(prefix =>
            namespaceName.Equals(prefix, StringComparison.Ordinal) ||
            namespaceName.StartsWith(prefix + ".", StringComparison.Ordinal));
    }

    private static bool IsDeclaredInSolution(IMethodSymbol method, string solutionDirectory) =>
        GetDeclarations(method).Any(reference =>
        {
            var relativePath = SourcePaths.ToSolutionRelative(solutionDirectory, reference.SyntaxTree.FilePath);
            return relativePath is not null && !SourcePaths.IsBuildOutput(relativePath);
        });

    public static bool IsSolutionMethod(IMethodSymbol method, string solutionDirectory, TraceLensConfig config) =>
        !IsExcluded(method, config) && IsDeclaredInSolution(method, solutionDirectory);

    /// <summary>
    /// 선언 위치를 돌려준다. 컴파일러가 만들어 주는 기본 생성자에는 선언 구문이 없으므로 그 타입의 선언 위치로 대신한다.
    /// </summary>
    internal static IEnumerable<SyntaxReference> GetDeclarations(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Length > 0
            ? method.DeclaringSyntaxReferences
            : method.MethodKind == MethodKind.Constructor
                ? method.ContainingType.DeclaringSyntaxReferences
                : [];
}

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

    /// <summary>람다 본문 안에서 나온 호출인지. 실행 여부를 이 자리에서 증명할 수 없어 확실도를 낮춘다.</summary>
    public bool ViaLambda { get; init; }

    /// <summary>
    /// <see cref="CallResolution.External"/>일 때 대상 타입의 네임스페이스와 이름.
    /// 엣지를 늘리는 데 쓰지 않고 "무엇을 보고도 해석하지 않았는지"를 집계하는 데만 쓴다.
    /// </summary>
    public string ExternalNamespace { get; init; } = string.Empty;

    public string ExternalType { get; init; } = string.Empty;
}

/// <summary>
/// 메서드 본문 안의 호출식을 심볼로 해석하고, 인터페이스 호출은 DI 등록으로 구현체까지 연결한다.
/// <c>new X()</c>도 생성자(.ctor) 호출이므로 <see cref="TraceLensConfig.IncludeObjectCreation"/>이 켜져 있으면 함께 모은다.
/// 로컬 함수 본문은 <see cref="CallableBodies"/>가 경계에서 멈추므로 부모의 호출로 잡히지 않는다.
/// 람다 본문은 순회하되 <see cref="ResolvedCall.ViaLambda"/>로 표시하고 확실도를 <see cref="CallConfidence.Estimated"/>로 낮춘다.
/// </summary>
public sealed class InvocationResolver(DiResolver diResolver, ILogger<InvocationResolver> logger)
{
    public IReadOnlyList<ResolvedCall> ResolveCalls(
        SemanticModel model,
        SyntaxNode methodSyntax,
        string solutionDirectory,
        TraceLensConfig config,
        CancellationToken cancellationToken = default,
        IReadOnlySet<LambdaKey>? excludedLambdas = null)
    {
        var result = new List<ResolvedCall>();
        var receiverNotes = new Dictionary<int, string>();
        foreach (var (syntax, lambda) in CallableBodies.EnumerateOwnNodes(methodSyntax, excludedLambdas))
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

            var call = Resolve(model, syntax, solutionDirectory, config, cancellationToken);
            var extra = ResolveOverrideCall(call, solutionDirectory, config);
            if (lambda is null)
            {
                result.Add(call);
                if (extra is not null)
                {
                    result.Add(extra);
                }

                continue;
            }

            if (!receiverNotes.TryGetValue(lambda.SpanStart, out var note))
            {
                note = DescribeLambdaReceiver(model, lambda, cancellationToken);
                receiverNotes[lambda.SpanStart] = note;
            }

            result.Add(call with
            {
                Confidence = CallConfidence.Estimated,
                Note = call.Note.Length == 0 ? note : note + " " + call.Note,
                ViaLambda = true
            });
            if (extra is not null)
            {
                result.Add(extra with
                {
                    Note = note + " " + extra.Note,
                    ViaLambda = true
                });
            }
        }

        return result;
    }

    /// <summary>
    /// virtual·abstract 대상 호출이면 솔루션 안 재정의로 가는 <b>추가</b> 엣지를 만든다.
    /// 기반 선언으로 가는 직접 엣지는 그대로 둔다 — 실제로 어느 쪽이 실행될지는 런타임 타입이 정한다.
    /// 재정의가 여럿이면 확정하지 않고 경계로만 남긴다.
    /// </summary>
    private ResolvedCall? ResolveOverrideCall(ResolvedCall call, string solutionDirectory, TraceLensConfig config)
    {
        if (call.Target is not { } target || call.Resolution != CallResolution.Direct)
        {
            return null;
        }

        if (!target.IsVirtual && !target.IsAbstract)
        {
            return null;
        }

        var resolved = diResolver.ResolveOverride(target);
        if (resolved.Method is null)
        {
            // 재정의를 확정하지 못했다는 사실 자체를 경계 목록에 남긴다.
            return resolved.Note.Contains("재정의가", StringComparison.Ordinal)
                ? new ResolvedCall(
                    null, call.DeclaredTarget, CallResolution.Ambiguous, CallConfidence.Estimated,
                    call.File, call.Line, resolved.Note)
                : null;
        }

        return IsSolutionMethod(resolved.Method, solutionDirectory, config)
            ? new ResolvedCall(
                resolved.Method,
                call.DeclaredTarget,
                CallResolution.SingleImplementation,
                CallConfidence.Estimated,
                call.File,
                call.Line,
                resolved.Note)
            : null;
    }

    /// <summary>람다를 받는 쪽을 사람이 읽을 한 줄로 적는다. 호출 인자로 넘어간 람다면 그 메서드, 변수에 담은 람다면 변수 이름이다.</summary>
    private static string DescribeLambdaReceiver(
        SemanticModel model, AnonymousFunctionExpressionSyntax lambda, CancellationToken cancellationToken)
    {
        if (lambda.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: { } owner } } &&
            owner is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
        {
            var symbolInfo = model.GetSymbolInfo(owner, cancellationToken);
            var receiver = (symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) as IMethodSymbol;
            var display = receiver is null
                ? owner is InvocationExpressionSyntax invocation ? invocation.Expression.ToString() : owner.ToString()
                : SymbolNames.GetDisplayName(receiver.ReducedFrom ?? receiver);
            return $"람다 본문 안의 호출. 실제 실행 여부는 이 람다를 받는 {display}가 결정한다.";
        }

        var variable = lambda.FirstAncestorOrSelf<VariableDeclaratorSyntax>()?.Identifier.Text
            ?? (lambda.Parent as AssignmentExpressionSyntax)?.Left.ToString();
        return variable is null
            ? "람다 본문 안의 호출. 이 람다가 언제 실행되는지는 이 자리에서 확정할 수 없다."
            : $"람다 본문 안의 호출. 실제 실행 여부는 대리자 변수 {variable}를 부르는 쪽이 결정한다.";
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
                    ? WithExternalOrigin(
                        new ResolvedCall(null, declaredTarget, CallResolution.External, CallConfidence.Certain, file, line, di.Note),
                        calledMethod)
                    : new ResolvedCall(null, declaredTarget, CallResolution.Ambiguous, CallConfidence.Estimated, file, line, di.Note);
            }

            return IsSolutionMethod(di.Method, solutionDirectory, config)
                ? new ResolvedCall(di.Method, declaredTarget, di.Resolution, di.Confidence, file, line, di.Note)
                : WithExternalOrigin(
                    new ResolvedCall(null, declaredTarget, CallResolution.External, CallConfidence.Certain, file, line,
                        "구현체가 솔루션 밖에 있다."),
                    di.Method);
        }

        if (!IsSolutionMethod(calledMethod, solutionDirectory, config))
        {
            return WithExternalOrigin(
                new ResolvedCall(
                    null, declaredTarget, CallResolution.External, CallConfidence.Certain, file, line,
                    IsExcluded(calledMethod, config) ? "excludeNamespacePrefixes로 제외한 범위다." : "솔루션 밖 호출이다."),
                calledMethod);
        }

        return new ResolvedCall(
            calledMethod, declaredTarget, CallResolution.Direct, CallConfidence.Certain, file, line, string.Empty);
    }

    /// <summary>
    /// External 호출에 대상 네임스페이스·타입을 붙인다.
    /// 해석 결과는 그대로 두고 집계용 정보만 더한다 — 엣지 수도 해석률도 바뀌지 않는다.
    /// </summary>
    private static ResolvedCall WithExternalOrigin(ResolvedCall call, IMethodSymbol? method)
    {
        var type = method?.ContainingType;
        if (type is null)
        {
            return call;
        }

        var containingNamespace = type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : string.Empty;
        return call with
        {
            ExternalNamespace = containingNamespace,
            ExternalType = type.Name
        };
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

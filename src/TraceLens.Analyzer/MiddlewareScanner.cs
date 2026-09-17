using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// Program.cs의 <c>app.UseXxx()</c> 순서를 그대로 파이프라인으로 만들고,
/// 솔루션 안에 있는 미들웨어는 <c>InvokeAsync</c> 본문을 열어 <c>_next</c> 앞뒤(요청/응답 구간)와 단락 지점을 읽는다.
/// </summary>
public sealed class MiddlewareScanner(ILogger<MiddlewareScanner> logger)
{
    /// <summary>프레임워크 미들웨어는 본문을 볼 수 없으니 알려진 설명을 쓴다.</summary>
    private static readonly Dictionary<string, (string Pre, string? Post, string? Status, string? Condition)> Known = new(StringComparer.Ordinal)
    {
        ["UseRouting"] = ("요청 경로에 맞는 엔드포인트를 고른다(실행은 하지 않는다)", null, null, null),
        ["UseAuthentication"] = ("토큰·쿠키를 검증해 ctx.User를 채운다(실패해도 여기서 끊지는 않는다)", null, null, null),
        ["UseAuthorization"] = ("엔드포인트의 [Authorize] 정책을 평가한다", null, "403", "정책 불충족"),
        ["UseCors"] = ("Origin 헤더를 보고 CORS 헤더를 붙인다. 프리플라이트면 여기서 끝낸다", null, "204", "OPTIONS 프리플라이트"),
        ["UseHttpsRedirection"] = ("http 요청이면 https로 돌린다", null, "307", "스킴이 http일 때"),
        ["UseHsts"] = ("HSTS 헤더를 붙인다", null, null, null),
        ["UseStaticFiles"] = ("정적 파일 요청이면 여기서 응답하고 끝낸다", null, "200", "정적 파일이 있을 때"),
        ["UseExceptionHandler"] = ("아래 전부를 감싼다", "예외를 잡아 오류 응답으로 바꾼다", null, null),
        ["UseDeveloperExceptionPage"] = ("아래 전부를 감싼다", "예외를 개발용 페이지로 바꾼다", null, null),
        ["UseStatusCodePages"] = ("아래 전부를 감싼다", "빈 오류 응답에 본문을 채운다", null, null),
        ["UseResponseCompression"] = ("응답 압축을 준비한다", "응답 본문을 압축한다", null, null),
        ["UseResponseCaching"] = ("캐시된 응답이 있으면 바로 돌려준다", "응답을 캐시에 넣는다", "200", "캐시 적중"),
        ["UseOutputCache"] = ("캐시된 응답이 있으면 바로 돌려준다", "응답을 캐시에 넣는다", "200", "캐시 적중"),
        ["UseRateLimiter"] = ("한도를 넘었는지 확인한다", null, "429", "한도 초과"),
        ["UseSession"] = ("세션을 읽어 컨텍스트에 붙인다", "세션 변경분을 저장한다", null, null),
        ["UseWebSockets"] = ("웹소켓 업그레이드 요청을 처리한다", null, "101", "업그레이드 요청"),
        ["UseForwardedHeaders"] = ("프록시가 붙인 헤더로 원본 IP·스킴을 복원한다", null, null, null),
        ["UseSwagger"] = ("Swagger 문서 요청이면 여기서 응답한다", null, "200", "/swagger 경로"),
        ["UseSwaggerUI"] = ("Swagger UI 요청이면 여기서 응답한다", null, "200", "/swagger 경로"),
        ["UseRequestLocalization"] = ("요청의 언어를 정한다", null, null, null),
        ["UseHttpLogging"] = ("요청을 로그로 남긴다", "응답을 로그로 남긴다", null, null)
    };

    public async Task<MiddlewarePipelineDocument?> ScanAsync(
        LoadedSolution loaded,
        TraceLensConfig config,
        string solutionDirectory,
        StartupFlowDocument? startupFlow,
        DataAccessScanResult? dataAccess,
        CancellationToken cancellationToken = default)
    {
        if (!config.IncludeMiddleware)
        {
            return null;
        }

        var program = await FindProgramDocumentAsync(loaded, cancellationToken);
        if (program is null)
        {
            logger.LogInformation("Program.cs를 찾지 못해 미들웨어 보기를 건너뛴다.");
            return null;
        }

        var root = await program.GetSyntaxRootAsync(cancellationToken);
        var model = await program.GetSemanticModelAsync(cancellationToken);
        if (root is null || model is null)
        {
            return null;
        }

        var relativeProgram = SourcePaths.ToSolutionRelative(solutionDirectory, program.FilePath)
            ?? program.FilePath ?? "Program.cs";
        var steps = new List<MiddlewareStepInfo>();
        var findings = new List<Finding>();
        var lifetimes = BuildLifetimeMap(startupFlow);

        foreach (var invocation in EnumerateApplicationCalls(root, model, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AppendStepAsync(
                invocation, model, loaded, config, solutionDirectory, relativeProgram,
                dataAccess, lifetimes, condition: null, steps, findings, cancellationToken);
        }

        MarkDeadSteps(steps);
        AddOrderFindings(steps, findings, relativeProgram);

        logger.LogInformation(
            "미들웨어 {StepCount}단계 · 점검 {FindingCount}건 ({File})",
            steps.Count,
            findings.Count,
            relativeProgram);
        return new MiddlewarePipelineDocument(relativeProgram, steps, findings, Catalog);
    }

    private async Task AppendStepAsync(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        LoadedSolution loaded,
        TraceLensConfig config,
        string solutionDirectory,
        string programFile,
        DataAccessScanResult? dataAccess,
        IReadOnlyDictionary<string, StartupLifetime> lifetimes,
        string? condition,
        List<MiddlewareStepInfo> steps,
        List<Finding> findings,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol symbol)
        {
            return;
        }

        var name = symbol.Name;

        // app.Run()/RunAsync()는 파이프라인 단계가 아니라 호스트를 띄우는 호출이다.
        if (name is "Run" or "RunAsync" or "StartAsync" or "StopAsync" or "WaitForShutdown" or "WaitForShutdownAsync"
            && invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var enclosingCondition = condition ?? GetEnclosingCondition(invocation);

        // UseWhen/MapWhen은 그 자체가 단계가 아니라 "조건"이다. 안쪽에서 등록하는 것들을 조건부 단계로 편다.
        // UseWhen/MapWhen과 IApplicationBuilder.Map(path, branch)은 그 자체가 단계가 아니라 "분기"다.
        if ((name is "UseWhen" or "MapWhen" ||
             (name == "Map" && invocation.ArgumentList.Arguments is [_, { Expression: AnonymousFunctionExpressionSyntax }, ..])) &&
            invocation.ArgumentList.Arguments.Count >= 2)
        {
            var predicate = invocation.ArgumentList.Arguments[0].Expression.ToString();
            var branch = invocation.ArgumentList.Arguments[1].Expression;
            foreach (var inner in EnumerateApplicationCalls(branch, model, cancellationToken))
            {
                await AppendStepAsync(
                    inner, model, loaded, config, solutionDirectory, programFile, dataAccess, lifetimes,
                    predicate, steps, findings, cancellationToken);
            }

            return;
        }

        var order = steps.Count + 1;
        var id = $"mw{order}";

        if (name == "UseMiddleware" && GetTypeArgument(invocation, model, cancellationToken) is { } middlewareType)
        {
            steps.Add(await BuildCustomStepAsync(
                id, order, middlewareType, invocation, loaded, solutionDirectory, programFile, line,
                dataAccess, lifetimes, enclosingCondition, findings, cancellationToken));
            return;
        }

        if (name == "Use" && invocation.ArgumentList.Arguments.Count > 0 &&
            invocation.ArgumentList.Arguments[0].Expression is AnonymousFunctionExpressionSyntax lambda)
        {
            var analysis = AnalyzeBody(lambda.Body, model, cancellationToken);
            steps.Add(new MiddlewareStepInfo(
                id, order, "app.Use(인라인)", MiddlewareType.Custom, programFile, line,
                null, programFile, line,
                analysis.Pre, analysis.Post, analysis.ShortCircuit,
                analysis.ContextAccesses, [], [], false, false, enclosingCondition,
                "Program.cs 안에 직접 쓴 인라인 미들웨어다."));
            return;
        }

        // Map*는 엔드포인트 "등록"이지 미들웨어가 아니다. 실행은 파이프라인 맨 끝에서 일어나므로 그 뒤 미들웨어도 실행된다.
        var isRegistration = name.StartsWith("Map", StringComparison.Ordinal);
        var isTerminal = name == "UseEndpoints" ||
                         (name == "Run" && invocation.ArgumentList.Arguments.Count > 0);
        var kind = isTerminal
            ? MiddlewareStepKind.Terminal
            : isRegistration
                ? MiddlewareStepKind.EndpointRegistration
                : MiddlewareStepKind.Middleware;
        var known = Known.TryGetValue(name, out var description) ? description : default;
        var pre = known.Pre ?? kind switch
        {
            MiddlewareStepKind.EndpointRegistration =>
                "엔드포인트를 등록한다. 실제 실행은 파이프라인 맨 끝에서 일어나므로 이 줄 뒤에 등록한 미들웨어도 실행된다",
            MiddlewareStepKind.Terminal => "라우트가 맞으면 엔드포인트(필터 → 액션)를 실행하고 요청을 끝낸다",
            _ => "프레임워크 미들웨어다. 본문은 어셈블리 안에 있어 열어 볼 수 없다"
        };
        steps.Add(new MiddlewareStepInfo(
            id, order, FormatCall(name, invocation), MiddlewareType.Framework, programFile, line,
            null, null, 0,
            pre, known.Post,
            known.Status is null ? null : new MiddlewareShortCircuit(known.Status, known.Condition ?? string.Empty, line),
            [], [], kind == MiddlewareStepKind.Middleware ? [] : ReadFilters(invocation), isTerminal, false,
            enclosingCondition,
            null)
        {
            Kind = kind
        });
    }

    private async Task<MiddlewareStepInfo> BuildCustomStepAsync(
        string id,
        int order,
        INamedTypeSymbol type,
        InvocationExpressionSyntax registration,
        LoadedSolution loaded,
        string solutionDirectory,
        string programFile,
        int registrationLine,
        DataAccessScanResult? dataAccess,
        IReadOnlyDictionary<string, StartupLifetime> lifetimes,
        string? condition,
        List<Finding> findings,
        CancellationToken cancellationToken)
    {
        var classId = SymbolNames.GetClassId(type);
        var reference = type.DeclaringSyntaxReferences.FirstOrDefault();
        var declaration = reference is null ? null : await reference.GetSyntaxAsync(cancellationToken);
        var document = reference is null ? null : loaded.Solution.GetDocument(reference.SyntaxTree);
        var model = document is null ? null : await document.GetSemanticModelAsync(cancellationToken);
        var file = SourcePaths.ToSolutionRelative(solutionDirectory, reference?.SyntaxTree.FilePath);

        var invoke = declaration?.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.Text is "InvokeAsync" or "Invoke");
        var analysis = invoke is null || model is null
            ? BodyAnalysis.Empty
            : AnalyzeBody((SyntaxNode?)invoke.Body ?? invoke.ExpressionBody, model, cancellationToken);

        var invokeLine = invoke?.GetLocation().GetLineSpan().StartLinePosition.Line + 1 ?? 0;
        var storeAccesses = dataAccess is null || invoke is null || model is null
            ? []
            : ReadStoreAccesses(dataAccess, model, invoke, cancellationToken);

        if (analysis.SilentReturnLine > 0 && file is not null)
        {
            findings.Add(new Finding(
                $"MW-005:{classId}", "MW-005", FindingSeverity.Critical, FindingConfidence.Certain,
                "next도 부르지 않고 응답도 쓰지 않는 분기가 있다",
                "그 분기로 빠지면 상태 코드 200에 빈 본문이 나간다. 클라이언트는 성공으로 읽고 로그에도 오류가 남지 않는다.",
                "응답 코드를 명시적으로 넣고 반환하거나 next를 부른다.",
                type.Name, file, analysis.SilentReturnLine, null, null, id, null,
                [new FindingEvidence("bad", "빠져나가는 분기", "next 호출도 응답 쓰기도 없다", $"{file}:{analysis.SilentReturnLine}")]));
        }

        if (analysis.HeaderAfterNextLine > 0 && file is not null)
        {
            findings.Add(new Finding(
                $"MW-006:{classId}", "MW-006", FindingSeverity.Warning, FindingConfidence.Certain,
                "응답 구간에서 헤더를 수정한다",
                "next 이후에는 응답 전송이 이미 시작됐을 수 있다. 그때 헤더를 건드리면 예외가 난다.",
                "Response.OnStarting 콜백에서 헤더를 넣는다.",
                type.Name, file, analysis.HeaderAfterNextLine, null, null, id, null,
                [new FindingEvidence("bad", "next 이후 헤더 수정", analysis.HeaderAfterNextText, $"{file}:{analysis.HeaderAfterNextLine}")]));
        }

        if (analysis.SyncWaitLine > 0 && file is not null)
        {
            findings.Add(new Finding(
                $"MW-008:{classId}", "MW-008", FindingSeverity.Warning, FindingConfidence.Certain,
                "미들웨어에서 동기로 기다린다",
                "모든 요청이 지나는 자리라 스레드풀 고갈로 바로 이어진다.",
                "await로 바꾼다.",
                type.Name, file, analysis.SyncWaitLine, null, null, id, null,
                [new FindingEvidence("bad", "동기 대기", analysis.SyncWaitText, $"{file}:{analysis.SyncWaitLine}")]));
        }

        foreach (var parameter in GetConstructorParameters(type))
        {
            var parameterTypeId = parameter.Type is INamedTypeSymbol named ? SymbolNames.GetClassId(named) : null;
            if (parameterTypeId is null || !lifetimes.TryGetValue(parameterTypeId, out var lifetime) ||
                lifetime != StartupLifetime.Scoped || file is null)
            {
                continue;
            }

            findings.Add(new Finding(
                $"MW-004:{classId}:{parameter.Name}", "MW-004", FindingSeverity.Critical, FindingConfidence.Certain,
                $"생성자가 Scoped 서비스({parameter.Type.Name})를 받는다",
                "미들웨어는 앱 시작 때 한 번 만들어진다. 생성자로 받은 Scoped 인스턴스는 첫 요청의 것이 그대로 굳는다.",
                "InvokeAsync 인자로 받으면 프레임워크가 요청마다 주입해 준다.",
                type.Name, file, invokeLine, null, null, id, null,
                [
                    new FindingEvidence("bad", "생성자 주입", $"{parameter.Type.Name} {parameter.Name}", $"{file}"),
                    new FindingEvidence("bad", "등록 수명", "Scoped", "시작 흐름에서 읽었다")
                ]));
        }

        return new MiddlewareStepInfo(
            id, order, type.Name, MiddlewareType.Custom, programFile, registrationLine,
            classId, file, invokeLine,
            analysis.Pre, analysis.Post, analysis.ShortCircuit,
            analysis.ContextAccesses, storeAccesses, [], false, false, condition,
            ReadSummary(declaration));
    }

    /* ------------------------------------------------------------------ 본문 분석 */

    private sealed record BodyAnalysis(
        string Pre,
        string? Post,
        MiddlewareShortCircuit? ShortCircuit,
        IReadOnlyList<ContextAccess> ContextAccesses,
        int SilentReturnLine,
        int HeaderAfterNextLine,
        string HeaderAfterNextText,
        int SyncWaitLine,
        string SyncWaitText)
    {
        public static BodyAnalysis Empty { get; } =
            new("본문을 읽지 못했다", null, null, [], 0, 0, string.Empty, 0, string.Empty);
    }

    private static BodyAnalysis AnalyzeBody(SyntaxNode? body, SemanticModel model, CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return BodyAnalysis.Empty;
        }

        var nextCall = body.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation => IsNextCall(invocation, model, cancellationToken));
        var nextPosition = nextCall?.SpanStart ?? int.MaxValue;

        // 단락 분기 안에서 하는 일(상태 코드 넣기·본문 쓰기)은 "요청 시 하는 일"이 아니라 끊을 때의 처리다.
        var branchSpans = body.DescendantNodes().OfType<IfStatementSyntax>()
            .Where(statement => !statement.Statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Any(invocation => IsNextCall(invocation, model, cancellationToken)))
            .Where(statement => statement.Statement.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any())
            .Select(statement => statement.Statement.Span)
            .ToArray();
        bool InShortCircuitBranch(SyntaxNode node) => branchSpans.Any(span => span.Contains(node.SpanStart));

        var accesses = new List<ContextAccess>();
        var preActions = new List<string>();
        var postActions = new List<string>();
        var headerAfterNextLine = 0;
        var headerAfterNextText = string.Empty;

        foreach (var node in body.DescendantNodes())
        {
            var after = node.SpanStart > nextPosition;
            var inBranch = InShortCircuitBranch(node);
            switch (node)
            {
                case AssignmentExpressionSyntax assignment when IsContextTarget(assignment.Left, out var target):
                    accesses.Add(new ContextAccess(ContextAccessMode.Write, target));
                    if (!inBranch)
                    {
                        (after ? postActions : preActions).Add($"{target} 설정");
                    }

                    if (after && target.StartsWith("Response.Headers", StringComparison.Ordinal))
                    {
                        headerAfterNextLine = assignment.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        headerAfterNextText = assignment.ToString();
                    }

                    break;

                case ElementAccessExpressionSyntax element when
                    element.Parent is not AssignmentExpressionSyntax && IsContextTarget(element, out var readTarget):
                    accesses.Add(new ContextAccess(ContextAccessMode.Read, readTarget));
                    break;

                case InvocationExpressionSyntax invocation when !IsNextCall(invocation, model, cancellationToken):
                    var label = DescribeInvocation(invocation);
                    if (label is not null && !inBranch)
                    {
                        (after ? postActions : preActions).Add(label);
                    }

                    break;
            }
        }

        var (shortCircuit, silentReturnLine) = FindShortCircuit(body, nextCall, model, cancellationToken);
        var syncWait = body.DescendantNodes().FirstOrDefault(node =>
            node is MemberAccessExpressionSyntax member && member.Name.Identifier.Text == "Result" ||
            node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Wait" } });

        var pre = preActions.Count > 0
            ? string.Join(" · ", preActions.Distinct().Take(4))
            : nextCall is not null ? "특별한 처리 없이 다음으로 넘긴다" : "본문에서 next를 부르지 않는다";
        var post = nextCall is null || postActions.Count == 0
            ? null
            : string.Join(" · ", postActions.Distinct().Take(4));

        return new BodyAnalysis(
            pre,
            post,
            shortCircuit,
            accesses.DistinctBy(access => $"{access.Mode}:{access.Target}").ToArray(),
            silentReturnLine,
            headerAfterNextLine,
            headerAfterNextText,
            syncWait?.GetLocation().GetLineSpan().StartLinePosition.Line + 1 ?? 0,
            syncWait?.ToString() ?? string.Empty);
    }

    /// <summary>
    /// <c>next</c>를 부르지 않고 끝나는 분기를 찾는다. 상태 코드를 넣으면 단락, 아무것도 안 하면 조용히 끝나는 자리(MW-005)다.
    /// </summary>
    private static (MiddlewareShortCircuit? ShortCircuit, int SilentReturnLine) FindShortCircuit(
        SyntaxNode body, InvocationExpressionSyntax? nextCall, SemanticModel model, CancellationToken cancellationToken)
    {
        MiddlewareShortCircuit? shortCircuit = null;
        var silentLine = 0;

        foreach (var statement in body.DescendantNodes().OfType<IfStatementSyntax>())
        {
            var branch = statement.Statement;
            if (branch.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Any(invocation => IsNextCall(invocation, model, cancellationToken)))
            {
                continue;
            }

            var returns = branch.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any();
            if (!returns)
            {
                continue;
            }

            var status = branch.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>()
                .Where(assignment => assignment.Left.ToString().EndsWith("StatusCode", StringComparison.Ordinal))
                .Select(assignment => assignment.Right.ToString())
                .FirstOrDefault();
            var writesBody = branch.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Any(invocation => invocation.ToString().Contains("Write", StringComparison.Ordinal));

            var line = statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (status is not null)
            {
                shortCircuit ??= new MiddlewareShortCircuit(
                    NormalizeStatus(status),
                    Shorten(statement.Condition.ToString()),
                    line);
                continue;
            }

            if (!writesBody && silentLine == 0)
            {
                silentLine = branch.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().First()
                    .GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            }
        }

        return (shortCircuit, silentLine);
    }

    private static bool IsNextCall(
        InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        var text = invocation.Expression.ToString();
        if (text.Contains("next", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return model.GetTypeInfo(invocation.Expression, cancellationToken).Type?.Name == "RequestDelegate";
    }

    private static bool IsContextTarget(ExpressionSyntax expression, out string target)
    {
        var text = expression.ToString();
        foreach (var key in new[] { "Items", "Response.Headers", "Response.StatusCode", "Request.Headers", "User" })
        {
            var index = text.IndexOf(key, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            target = text[index..];
            return true;
        }

        target = string.Empty;
        return false;
    }

    private static string? DescribeInvocation(InvocationExpressionSyntax invocation)
    {
        var text = invocation.Expression.ToString();
        var name = text.Split('.').Last();
        return name switch
        {
            "WriteAsync" or "WriteAsJsonAsync" => "응답 본문을 쓴다",
            "LogInformation" or "LogWarning" or "LogError" or "LogDebug" => "로그를 남긴다",
            "OnStarting" => "응답 시작 직전 콜백을 건다",
            _ when name.StartsWith("Get", StringComparison.Ordinal) && text.Contains("Cache", StringComparison.OrdinalIgnoreCase) =>
                "캐시를 읽는다",
            _ when name.StartsWith("Set", StringComparison.Ordinal) && text.Contains("Cache", StringComparison.OrdinalIgnoreCase) =>
                "캐시에 쓴다",
            _ => null
        };
    }

    private static IReadOnlyList<string> ReadStoreAccesses(
        DataAccessScanResult dataAccess,
        SemanticModel model,
        MethodDeclarationSyntax invoke,
        CancellationToken cancellationToken)
    {
        if (model.GetDeclaredSymbol(invoke, cancellationToken) is not IMethodSymbol symbol)
        {
            return [];
        }

        var nodeId = SymbolNames.GetNodeId(symbol);
        return dataAccess.Accesses
            .Where(access => access.MethodNodeId == nodeId)
            .Select(access => $"{access.DataSource} · {access.Statement}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /* ------------------------------------------------------------------ 순서 규칙 */

    /// <summary>
    /// 요청을 끝내는 종단(<c>UseEndpoints</c>·<c>app.Run(delegate)</c>) 뒤에 등록한 미들웨어만 "실행되지 않는다"로 본다.
    /// <c>MapControllers()</c> 같은 엔드포인트 등록은 종단이 아니다 — 엔드포인트 실행은 파이프라인 맨 끝이다.
    /// </summary>
    private static void MarkDeadSteps(List<MiddlewareStepInfo> steps)
    {
        var terminal = steps.FindIndex(step => step.Kind == MiddlewareStepKind.Terminal);
        if (terminal < 0)
        {
            return;
        }

        for (var index = terminal + 1; index < steps.Count; index++)
        {
            if (steps[index].Kind == MiddlewareStepKind.Middleware)
            {
                steps[index] = steps[index] with { IsDead = true };
            }
        }
    }

    private static void AddOrderFindings(
        IReadOnlyList<MiddlewareStepInfo> steps, List<Finding> findings, string programFile)
    {
        int IndexOf(Func<MiddlewareStepInfo, bool> predicate) =>
            steps.ToList().FindIndex(step => predicate(step));

        var exception = IndexOf(step => step.Name.Contains("Exception", StringComparison.OrdinalIgnoreCase));
        if (exception > 1)
        {
            var step = steps[exception];
            findings.Add(new Finding(
                "MW-001", "MW-001", FindingSeverity.Critical, FindingConfidence.Certain,
                $"예외 처리 미들웨어가 {exception + 1}번째에 있다",
                "그보다 앞에 등록된 미들웨어에서 난 예외는 잡히지 않는다.",
                "파이프라인 맨 앞으로 옮긴다.",
                step.Name, programFile, step.RegistrationLine, null, null, step.Id, null,
                [new FindingEvidence("bad", "현재 위치", $"{exception + 1}번째 단계", $"{programFile}:{step.RegistrationLine}")]));
        }

        var routing = IndexOf(step => step.Name.StartsWith("UseRouting", StringComparison.Ordinal));
        var authentication = IndexOf(step => step.Name.StartsWith("UseAuthentication", StringComparison.Ordinal));
        var authorization = IndexOf(step => step.Name.StartsWith("UseAuthorization", StringComparison.Ordinal));
        var cors = IndexOf(step => step.Name.StartsWith("UseCors", StringComparison.Ordinal));

        if (authentication >= 0 && authorization >= 0 && authorization < authentication)
        {
            findings.Add(Order("MW-002", FindingSeverity.Critical,
                "UseAuthorization이 UseAuthentication보다 먼저 등록됐다",
                "인가를 평가할 때 ctx.User가 아직 비어 있다. 정책이 통과되거나 항상 실패한다.",
                "UseAuthentication → UseAuthorization 순서로 바꾼다.",
                steps[authorization], programFile,
                $"현재 순서: {authorization + 1}번째 UseAuthorization → {authentication + 1}번째 UseAuthentication"));
        }

        if (routing >= 0 && authorization >= 0 && authorization < routing)
        {
            findings.Add(Order("MW-002", FindingSeverity.Critical,
                "UseAuthorization이 UseRouting보다 먼저 등록됐다",
                "엔드포인트가 정해지기 전이라 [Authorize] 메타데이터를 읽을 수 없다.",
                "UseRouting 뒤로 옮긴다.",
                steps[authorization], programFile,
                $"현재 순서: {authorization + 1}번째 UseAuthorization → {routing + 1}번째 UseRouting"));
        }

        if (routing >= 0 && cors >= 0 && cors < routing)
        {
            findings.Add(Order("MW-003", FindingSeverity.Warning,
                "UseCors가 UseRouting보다 먼저 등록됐다",
                "엔드포인트별 CORS 정책([EnableCors])이 적용되지 않고 기본 정책만 걸린다.",
                "UseRouting 뒤, UseAuthorization 앞으로 옮긴다.",
                steps[cors], programFile,
                $"현재 순서: {cors + 1}번째 UseCors → {routing + 1}번째 UseRouting"));
        }

        foreach (var step in steps.Where(step => step.IsDead))
        {
            findings.Add(new Finding(
                $"MW-012:{step.Id}", "MW-012", FindingSeverity.Warning, FindingConfidence.Certain,
                $"{step.Name}이(가) UseEndpoints/app.Run(delegate) 뒤에 등록되어 실행되지 않는다",
                "앞 단계에서 요청이 끝나므로 이 미들웨어에는 요청이 오지 않는다.",
                "종단(UseEndpoints·app.Run) 앞으로 옮기거나, 필요 없으면 등록을 지운다.",
                step.Name, programFile, step.RegistrationLine, null, null, step.Id, null,
                [new FindingEvidence("bad", "등록 위치", $"{step.Order}번째 · 종단 뒤", $"{programFile}:{step.RegistrationLine}")]));
        }
    }

    private static Finding Order(
        string ruleId, FindingSeverity severity, string what, string why, string fix,
        MiddlewareStepInfo step, string programFile, string evidence) => new(
        $"{ruleId}:{step.Id}", ruleId, severity, FindingConfidence.Certain, what, why, fix,
        step.Name, programFile, step.RegistrationLine, null, null, step.Id, null,
        [new FindingEvidence("bad", "등록 순서", evidence, $"{programFile}:{step.RegistrationLine}")]);

    /* ------------------------------------------------------------------ 도우미 */

    private static IReadOnlyDictionary<string, StartupLifetime> BuildLifetimeMap(StartupFlowDocument? startupFlow)
    {
        var map = new Dictionary<string, StartupLifetime>(StringComparer.Ordinal);
        if (startupFlow is null)
        {
            return map;
        }

        void Walk(IReadOnlyList<StartupStep> steps)
        {
            foreach (var step in steps)
            {
                if (step.Lifetime is { } lifetime)
                {
                    if (step.ServiceTypeId is not null)
                    {
                        map[step.ServiceTypeId] = lifetime;
                    }

                    if (step.ImplementationTypeId is not null)
                    {
                        map[step.ImplementationTypeId] = lifetime;
                    }
                }

                Walk(step.Children);
            }
        }

        Walk(startupFlow.ServiceSteps);
        return map;
    }

    private static IEnumerable<IParameterSymbol> GetConstructorParameters(INamedTypeSymbol type) => type.Constructors
        .Where(constructor => !constructor.IsStatic && constructor.Parameters.Length > 0)
        .SelectMany(constructor => constructor.Parameters)
        .Where(parameter => parameter.Type.Name != "RequestDelegate");

    private static IEnumerable<InvocationExpressionSyntax> EnumerateApplicationCalls(
        SyntaxNode root, SemanticModel model, CancellationToken cancellationToken) => root
        .DescendantNodesAndSelf()
        .OfType<InvocationExpressionSyntax>()
        .Where(invocation => IsApplicationCall(invocation, model, cancellationToken))
        .OrderBy(invocation => invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Name.SpanStart
            : invocation.SpanStart);

    private static bool IsApplicationCall(
        InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var qualifier = model.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
        if (qualifier is INamedTypeSymbol or INamespaceSymbol)
        {
            return false;
        }

        var type = model.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
        if (type is null)
        {
            return false;
        }

        return Implements(type, "IApplicationBuilder") || Implements(type, "IEndpointRouteBuilder");
    }

    private static bool Implements(ITypeSymbol type, string name) =>
        type.Name == name ||
        (type is INamedTypeSymbol named && named.AllInterfaces.Any(item => item.Name == name));

    private static INamedTypeSymbol? GetTypeArgument(
        InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        var generic = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name as GenericNameSyntax,
            GenericNameSyntax name => name,
            _ => null
        };
        var argument = generic?.TypeArgumentList.Arguments.FirstOrDefault();
        return argument is null ? null : model.GetTypeInfo(argument, cancellationToken).Type as INamedTypeSymbol;
    }

    /// <summary>엔드포인트 매핑에 붙은 필터를 읽는다(<c>AddEndpointFilter&lt;T&gt;</c> 체이닝).</summary>
    private static IReadOnlyList<EndpointFilterInfo> ReadFilters(InvocationExpressionSyntax invocation)
    {
        var filters = new List<EndpointFilterInfo>();
        for (var current = invocation.Parent; current is not null; current = current.Parent)
        {
            if (current is not InvocationExpressionSyntax outer ||
                outer.Expression is not MemberAccessExpressionSyntax member)
            {
                continue;
            }

            var name = member.Name.ToString();
            if (name.StartsWith("AddEndpointFilter", StringComparison.Ordinal) ||
                name.StartsWith("RequireAuthorization", StringComparison.Ordinal))
            {
                filters.Add(new EndpointFilterInfo(
                    name,
                    outer.ArgumentList.Arguments.Count > 0 ? outer.ArgumentList.ToString() : "인자 없음",
                    name.StartsWith("RequireAuthorization", StringComparison.Ordinal) ? "403" : null));
            }
        }

        return filters;
    }

    private static string? GetEnclosingCondition(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is IfStatementSyntax ifStatement)
            {
                return Shorten(ifStatement.Condition.ToString());
            }
        }

        return null;
    }

    private static string FormatCall(string name, InvocationExpressionSyntax invocation)
    {
        var literals = invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Select(literal => literal.ToString());
        var joined = string.Join(", ", literals);
        return joined.Length > 0 ? $"{name}({joined})" : name;
    }

    private static string NormalizeStatus(string text)
    {
        var trimmed = text.Trim();
        var index = trimmed.LastIndexOf('.');
        return index >= 0 ? MapStatusName(trimmed[(index + 1)..]) : trimmed;
    }

    private static string MapStatusName(string name) => name switch
    {
        "Unauthorized" => "401",
        "Forbidden" => "403",
        "BadRequest" => "400",
        "TooManyRequests" => "429",
        "ServiceUnavailable" => "503",
        "NotFound" => "404",
        "Conflict" => "409",
        _ => name
    };

    private static string Shorten(string text) =>
        text.Length <= 60 ? text : text[..57] + "…";

    /// <summary>클래스 XML 주석의 첫 줄을 메모로 쓴다. 없으면 null.</summary>
    private static string? ReadSummary(SyntaxNode? declaration)
    {
        var trivia = declaration?.GetLeadingTrivia()
            .Select(item => item.ToString())
            .FirstOrDefault(text => text.Contains("<summary>", StringComparison.Ordinal));
        if (trivia is null)
        {
            return null;
        }

        var lines = trivia.Split('\n')
            .Select(line => line.Trim().TrimStart('/', ' '))
            .Select(line => line.Replace("<summary>", string.Empty).Replace("</summary>", string.Empty).Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('<'))
            .ToArray();
        return lines.Length > 0 ? lines[0] : null;
    }

    private static async Task<Document?> FindProgramDocumentAsync(
        LoadedSolution loaded, CancellationToken cancellationToken)
    {
        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            var candidate = project.Documents.FirstOrDefault(document =>
                document.SourceCodeKind == SourceCodeKind.Regular &&
                string.Equals(Path.GetFileName(document.FilePath), "Program.cs", StringComparison.OrdinalIgnoreCase));
            if (candidate is null)
            {
                continue;
            }

            var text = (await candidate.GetTextAsync(cancellationToken)).ToString();
            if (text.Contains("CreateBuilder", StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>미들웨어 규칙 카탈로그. 이번 단계에서 검사하지 않는 규칙도 <c>Enabled=false</c>로 함께 싣는다.</summary>
    public static IReadOnlyList<RuleInfo> Catalog { get; } =
    [
        new("MW-001", "순서", "예외 처리 미들웨어가 앞쪽이 아니다", FindingSeverity.Critical, FindingConfidence.Certain,
            "예외를 잡는 미들웨어보다 앞에 등록된 미들웨어의 예외는 잡히지 않는다.", false, true),
        new("MW-002", "순서", "인증·인가·라우팅 순서가 어긋남", FindingSeverity.Critical, FindingConfidence.Certain,
            "UseRouting → UseAuthentication → UseAuthorization 순서를 벗어나면 정책이 적용되지 않는다.", false, true),
        new("MW-003", "순서", "UseCors 위치가 잘못됨", FindingSeverity.Warning, FindingConfidence.Certain,
            "UseRouting 앞에 있으면 엔드포인트별 CORS 정책이 적용되지 않는다.", false, true),
        new("MW-004", "구성", "미들웨어 생성자에 Scoped 서비스 주입", FindingSeverity.Critical, FindingConfidence.Certain,
            "미들웨어는 Singleton으로 한 번 만들어진다. 생성자로 받은 Scoped 서비스는 첫 요청의 것이 굳는다.", false, true),
        new("MW-005", "동작", "next도 부르지 않고 응답도 쓰지 않는 분기", FindingSeverity.Critical, FindingConfidence.Certain,
            "요청이 조용히 끝나 빈 200이 나간다.", false, true),
        new("MW-006", "동작", "응답 시작 후 헤더 수정", FindingSeverity.Warning, FindingConfidence.Certain,
            "next 이후에 Response.Headers를 건드린다. 이미 전송이 시작됐으면 예외가 난다.", false, true),
        new("MW-007", "동작", "요청 본문을 버퍼링 없이 다시 읽음", FindingSeverity.Warning, FindingConfidence.Estimated,
            "Request.Body는 한 번만 읽을 수 있다.", false, false),
        new("MW-008", "동작", "미들웨어에서 동기 대기", FindingSeverity.Warning, FindingConfidence.Certain,
            "모든 요청이 지나는 자리라 스레드풀 고갈로 이어진다.", false, true),
        new("MW-009", "구성", "가벼운 엔드포인트가 무거운 미들웨어를 전부 통과", FindingSeverity.Info, FindingConfidence.Estimated,
            "헬스체크·메트릭이 인증·세션 조회를 그대로 탄다.", false, false),
        new("MW-010", "동작", "예외를 삼키고 정상 응답으로 만든다", FindingSeverity.Warning, FindingConfidence.Estimated,
            "catch에서 로그만 남기고 200을 반환한다.", false, false),
        new("MW-011", "구성", "UseWhen 조건과 실제 라우트가 어긋남", FindingSeverity.Warning, FindingConfidence.Estimated,
            "조건 경로에 해당하는 엔드포인트가 없다.", false, false),
        new("MW-012", "순서", "등록됐지만 실행되지 않는 미들웨어", FindingSeverity.Warning, FindingConfidence.Certain,
            "종단(UseEndpoints·app.Run(delegate)) 뒤에 등록되어 실행 순서에 들어가지 않는다. MapControllers 같은 엔드포인트 등록은 종단이 아니므로 그 뒤 미들웨어는 정상 실행된다.", false, true)
    ];
}

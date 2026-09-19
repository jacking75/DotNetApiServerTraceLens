using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// <c>app.MapGet("/orders", handler)</c> 같은 Minimal API 등록식에서 HTTP 진입점을 찾는다.
/// 이름만 <c>MapGet</c>인 사용자 메서드를 잡지 않도록, 대상 메서드가
/// <c>Microsoft.AspNetCore</c>의 <c>IEndpointRouteBuilder</c> 확장인지 심볼로 확인한다.
/// 라우트는 <see cref="SemanticModel.GetConstantValue(SyntaxNode, CancellationToken)"/>로 상수만 확정하고,
/// <c>MapGroup</c> 접두사는 지역 변수·메서드 인자 바인딩을 따라가 합친다.
/// 동적 문자열·복잡한 분기·외부 패키지의 자체 라우팅 DSL은 미지원으로 남긴다.
/// </summary>
public sealed class MinimalApiEntryPointScanner(ILogger<MinimalApiEntryPointScanner> logger)
{
    /// <summary><c>app.MapFeatureEndpoints()</c>처럼 솔루션 안 확장 메서드에 숨은 등록을 따라갈 최대 깊이.</summary>
    private const int MaxRegistrarDepth = 4;

    private const string AspNetCoreNamespace = "Microsoft.AspNetCore";
    private const string EndpointRouteBuilder = "IEndpointRouteBuilder";

    private static readonly Dictionary<string, string> MapMethodVerbs = new(StringComparer.Ordinal)
    {
        ["MapGet"] = "GET",
        ["MapPost"] = "POST",
        ["MapPut"] = "PUT",
        ["MapDelete"] = "DELETE",
        ["MapPatch"] = "PATCH"
    };

    public async Task<MinimalApiScanResult> ScanAsync(
        LoadedSolution loaded,
        TraceLensConfig config,
        string solutionDirectory,
        CancellationToken cancellationToken = default)
    {
        var registrations = new List<Registration>();
        var notes = new List<string>();

        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            var projectKey = project.AssemblyName;
            foreach (var document in project.Documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = SourcePaths.ToSolutionRelative(solutionDirectory, document.FilePath);
                if (relativePath is null || SourcePaths.IsBuildOutput(relativePath))
                {
                    continue;
                }

                var text = (await document.GetTextAsync(cancellationToken)).ToString();
                if (!text.Contains(".Map", StringComparison.Ordinal))
                {
                    continue;
                }

                var semantics = await loaded.GetSemanticsAsync(document, cancellationToken);
                if (semantics is null)
                {
                    continue;
                }

                var (root, model) = (semantics.Root, semantics.Model);

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var registration = TryReadRegistration(model, invocation, projectKey, relativePath, cancellationToken);
                    if (registration is not null)
                    {
                        registrations.Add(registration);
                    }
                }
            }
        }

        if (registrations.Count == 0)
        {
            return new MinimalApiScanResult([], notes);
        }

        // 접두사를 아직 못 채운 등록은 그 등록을 감싼 메서드(등록자)를 부르는 쪽에서 접두사를 받아야 한다.
        var registrarsByMethod = registrations
            .Where(item => item.PrefixOwner is not null)
            .GroupBy(item => item.PrefixOwner!.ToDisplayString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var results = new List<ScannedEntryPoint>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        // 등록자 이름 → 그 등록자를 부르는 호출들. 매번 전체 등록을 훑으면 등록 수 × 등록자 수가 된다.
        var callsByTarget = registrations
            .SelectMany(item => item.RegistrarCalls)
            .GroupBy(call => call.Target.ToDisplayString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<RegistrarCall>)group.ToArray(), StringComparer.Ordinal);

        foreach (var registration in registrations.Where(item => item.PrefixOwner is null && item.HttpMethods.Count > 0))
        {
            Emit(registration, registration.LocalPrefix, results, usedIds, notes, [], registration.GroupPrefix);
        }

        // 등록자를 호출하는 쪽의 접두사를 따라 내려간다. 순환 방문은 막고, 제한을 넘으면 미해석 사유로 남긴다.
        foreach (var (methodKey, items) in registrarsByMethod)
        {
            var registrar = items[0].PrefixOwner!;
            var callers = EnumerateRegistrarCallSites(
                callsByTarget, methodKey, 0, new HashSet<string>(StringComparer.Ordinal)).ToArray();
            if (callers.Length == 0)
            {
                foreach (var item in items.Where(item => item.HttpMethods.Count > 0))
                {
                    var note = $"이 등록을 감싼 메서드 {registrar.Name}의 호출 지점을 찾지 못해 라우트 접두사를 확정하지 못했다.";
                    Emit(item, item.LocalPrefix, results, usedIds, notes, [note], item.GroupPrefix);
                }

                continue;
            }

            foreach (var caller in callers)
            {
                foreach (var item in items.Where(item => item.HttpMethods.Count > 0))
                {
                    Emit(
                        item,
                        EndpointIdentity.Append(caller.Prefix, item.LocalPrefix),
                        results,
                        usedIds,
                        notes,
                        [
                            .. caller.Notes,
                            $"라우트 접두사를 등록자 {registrar.Name}의 호출 지점에서 채웠다(추정)."
                        ],
                        EndpointIdentity.Append(caller.Prefix, item.GroupPrefix));
                }
            }
        }

        logger.LogInformation("Minimal API 진입점 {Count}개를 찾았다.", results.Count);
        return new MinimalApiScanResult(results, notes);
    }

    /// <summary>등록자 메서드를 호출하는 지점을 따라 접두사를 모은다. 깊이 제한을 넘으면 그 사유를 남긴다.</summary>
    private static IEnumerable<(string Prefix, IReadOnlyList<string> Notes)> EnumerateRegistrarCallSites(
        IReadOnlyDictionary<string, IReadOnlyList<RegistrarCall>> callsByTarget,
        string registrarKey,
        int depth,
        HashSet<string> visited)
    {
        if (!visited.Add(registrarKey))
        {
            yield break;
        }

        if (depth >= MaxRegistrarDepth)
        {
            yield return (string.Empty, new[] { $"등록 추적 깊이 {MaxRegistrarDepth}를 넘어 접두사를 확정하지 못했다." });
            yield break;
        }

        if (!callsByTarget.TryGetValue(registrarKey, out var calls) || calls.Count == 0)
        {
            yield break;
        }

        foreach (var call in calls)
        {
            if (call.PrefixOwner is null)
            {
                yield return (call.Prefix, call.Notes);
                continue;
            }

            foreach (var outer in EnumerateRegistrarCallSites(callsByTarget, call.PrefixOwner.ToDisplayString(), depth + 1, visited))
            {
                yield return (EndpointIdentity.Append(outer.Prefix, call.Prefix), [.. outer.Notes, .. call.Notes]);
            }
        }
    }

    private void Emit(
        Registration registration,
        string route,
        List<ScannedEntryPoint> results,
        HashSet<string> usedIds,
        List<string> scanNotes,
        IReadOnlyList<string> extraNotes,
        string groupPrefix)
    {
        var handler = registration.Handler;
        if (handler is null)
        {
            var message = $"{registration.File}:{registration.Line}의 {registration.HttpMethods.FirstOrDefault() ?? "MAP"} {route} 핸들러를 확정하지 못했다.";
            if (!scanNotes.Contains(message))
            {
                scanNotes.Add(message);
            }

            return;
        }

        var normalized = EndpointIdentity.NormalizeRoute(route);
        foreach (var httpMethod in registration.HttpMethods)
        {
            var endpointId = EndpointIdentity.Disambiguate(
                EndpointIdentity.Create(registration.ProjectKey, httpMethod, normalized, handler.NodeId),
                usedIds);
            var notes = registration.Notes.Concat(extraNotes).Distinct(StringComparer.Ordinal).ToArray();
            results.Add(new ScannedEntryPoint(
                handler,
                new EntryPoint(handler.NodeId, httpMethod, route.StartsWith('/') ? route : "/" + route)
                {
                    EndpointId = endpointId,
                    NormalizedRoute = normalized,
                    Source = EndpointSource.MinimalApi,
                    HandlerKind = registration.HandlerKind,
                    Confidence = registration.Confidence == CallConfidence.Certain && notes.Length == 0
                        ? CallConfidence.Certain
                        : CallConfidence.Estimated,
                    RegistrationFile = registration.File,
                    RegistrationLine = registration.Line,
                    HandlerDisplayName = handler.DisplayName,
                    ProjectKey = registration.ProjectKey,
                    GroupKey = groupPrefix,
                    Notes = notes
                }));
        }
    }

    private Registration? TryReadRegistration(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        string projectKey,
        string file,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        var name = memberAccess.Name.Identifier.ValueText;
        var target = ResolveTarget(model, invocation, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        // 솔루션 안 확장 메서드에 등록이 숨어 있는 경우(app.MapFeatureEndpoints())는 접두사만 계산해 두고 나중에 잇는다.
        if (IsSolutionRegistrar(target))
        {
            var registrarChain = ResolveBuilderChain(model, memberAccess.Expression, cancellationToken);
            return new Registration(projectKey, file, line, [], null, HandlerKind.Unresolved, CallConfidence.Certain, [])
            {
                LocalPrefix = registrarChain.Prefix,
                GroupPrefix = registrarChain.Prefix,
                PrefixOwner = null,
                RegistrarCalls =
                [
                    new RegistrarCall(
                        target.ReducedFrom ?? target,
                        registrarChain.Prefix,
                        registrarChain.Complete ? null : FindEnclosingMethod(model, invocation, cancellationToken),
                        registrarChain.Notes)
                ]
            };
        }

        if (!IsEndpointMapping(target))
        {
            return null;
        }

        var httpMethods = ReadHttpMethods(model, name, invocation, cancellationToken);
        if (httpMethods.Count == 0)
        {
            return null;
        }

        var notes = new List<string>();
        var confidence = CallConfidence.Certain;

        var routeArgument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        var routeConstant = routeArgument is null
            ? null
            : model.GetConstantValue(routeArgument, cancellationToken).Value as string;
        if (routeConstant is null)
        {
            notes.Add("라우트가 상수가 아니라 소스에 적힌 문자열을 그대로 표시한다.");
            confidence = CallConfidence.Estimated;
        }

        var chain = ResolveBuilderChain(model, memberAccess.Expression, cancellationToken);
        notes.AddRange(chain.Notes);
        if (!chain.Complete)
        {
            confidence = CallConfidence.Estimated;
        }

        var localRoute = EndpointIdentity.Append(chain.Prefix, routeConstant ?? routeArgument?.ToString() ?? string.Empty);
        var (handler, handlerKind, handlerNotes) = ResolveHandler(model, invocation, projectKey, cancellationToken);
        notes.AddRange(handlerNotes);
        if (handler is null)
        {
            confidence = CallConfidence.Estimated;
        }

        return new Registration(projectKey, file, line, httpMethods, handler, handlerKind, confidence, notes)
        {
            LocalPrefix = localRoute,
            GroupPrefix = chain.Prefix,
            PrefixOwner = chain.Complete ? null : FindEnclosingMethod(model, invocation, cancellationToken),
            RegistrarCalls = []
        };
    }

    private static IMethodSymbol? ResolveTarget(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        return info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    /// <summary>ASP.NET Core의 <c>IEndpointRouteBuilder</c> 확장인 Map* 메서드인지 심볼로 확인한다.</summary>
    private static bool IsEndpointMapping(IMethodSymbol method)
    {
        var target = method.ReducedFrom ?? method;
        var namespaceName = target.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!namespaceName.StartsWith(AspNetCoreNamespace, StringComparison.Ordinal))
        {
            return false;
        }

        return HasEndpointRouteBuilderReceiver(target) &&
               (MapMethodVerbs.ContainsKey(target.Name) || target.Name is "MapMethods" or "Map");
    }

    /// <summary>솔루션 안에 선언된, <c>IEndpointRouteBuilder</c>를 받는 확장 메서드인지.</summary>
    private static bool IsSolutionRegistrar(IMethodSymbol method)
    {
        var target = method.ReducedFrom ?? method;
        if (target.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        return HasEndpointRouteBuilderReceiver(target);
    }

    private static bool HasEndpointRouteBuilderReceiver(IMethodSymbol target)
    {
        var receiver = target.IsExtensionMethod && target.Parameters.Length > 0
            ? target.Parameters[0].Type
            : target.ReceiverType;
        return IsEndpointRouteBuilder(receiver);
    }

    private static bool IsEndpointRouteBuilder(ITypeSymbol? type) =>
        type is not null &&
        (type.Name == EndpointRouteBuilder ||
         type.AllInterfaces.Any(item => item.Name == EndpointRouteBuilder));

    private static IReadOnlyList<string> ReadHttpMethods(
        SemanticModel model,
        string name,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (MapMethodVerbs.TryGetValue(name, out var verb))
        {
            return [verb];
        }

        if (name == "Map")
        {
            return ["ANY"];
        }

        if (name != "MapMethods" || invocation.ArgumentList.Arguments.Count < 2)
        {
            return [];
        }

        var methodsArgument = invocation.ArgumentList.Arguments[1].Expression;
        var values = methodsArgument.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Select(expression => model.GetConstantValue(expression, cancellationToken).Value as string)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length > 0 ? values : ["ANY"];
    }

    /// <summary>핸들러 인자(마지막 인자)를 실행 가능한 callable로 바꾼다.</summary>
    private (AnalysisCallable? Handler, HandlerKind Kind, IReadOnlyList<string> Notes) ResolveHandler(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        string projectKey,
        CancellationToken cancellationToken)
    {
        var argument = invocation.ArgumentList.Arguments.LastOrDefault()?.Expression;
        if (argument is null)
        {
            return (null, HandlerKind.Unresolved, ["핸들러 인자가 없다."]);
        }

        if (argument is AnonymousFunctionExpressionSyntax lambda)
        {
            if (model.GetSymbolInfo(lambda, cancellationToken).Symbol is not IMethodSymbol lambdaSymbol)
            {
                return (null, HandlerKind.Unresolved, ["람다 핸들러의 심볼을 해석하지 못했다."]);
            }

            var enclosing = FindEnclosingMethod(model, invocation, cancellationToken);
            return (
                AnalysisCallable.ForLambda(
                    lambdaSymbol,
                    lambda,
                    projectKey,
                    DescribeLambda(invocation),
                    enclosing is null ? null : CallableIds.ForMethod(enclosing)),
                HandlerKind.Lambda,
                []);
        }

        var info = model.GetSymbolInfo(argument, cancellationToken);
        var handlerSymbol = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (handlerSymbol is null)
        {
            logger.LogDebug("Minimal API 핸들러를 해석하지 못했다: {Argument}", argument);
            return (null, HandlerKind.Unresolved, [$"핸들러 {argument}를 메서드로 해석하지 못했다."]);
        }

        var declaration = handlerSymbol.DeclaringSyntaxReferences.Length > 0
            ? handlerSymbol.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken)
            : null;
        if (declaration is null)
        {
            return (null, HandlerKind.Unresolved, ["핸들러 선언이 솔루션 밖에 있다."]);
        }

        if (handlerSymbol.MethodKind == MethodKind.LocalFunction)
        {
            var enclosing = handlerSymbol.ContainingSymbol as IMethodSymbol;
            return (
                AnalysisCallable.ForLocalFunction(
                    handlerSymbol,
                    declaration,
                    projectKey,
                    enclosing is null ? null : CallableIds.ForMethod(enclosing)),
                HandlerKind.LocalFunction,
                []);
        }

        return (
            AnalysisCallable.ForMethod(handlerSymbol, declaration, projectKey),
            HandlerKind.MethodGroup,
            []);
    }

    private static string DescribeLambda(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression is MemberAccessExpressionSyntax member
            ? member.Name.Identifier.ValueText
            : "Map";
        var route = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString().Trim('"') ?? string.Empty;
        return $"{name} {route} 핸들러";
    }

    /// <summary>
    /// <c>app.MapGroup("/api").MapGroup("/v1")</c>, <c>var g = app.MapGroup("/api"); g.MapGet(...)</c>처럼
    /// 수신 객체를 따라가며 정적 접두사를 합친다. 메서드 인자에서 온 빌더면 <c>Complete=false</c>로 표시한다.
    /// </summary>
    private BuilderChain ResolveBuilderChain(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        int depth = 0)
    {
        if (depth > MaxRegistrarDepth)
        {
            return new BuilderChain(string.Empty, true, [$"MapGroup 추적 깊이 {MaxRegistrarDepth}를 넘었다."]);
        }

        switch (receiver)
        {
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax member &&
                     member.Name.Identifier.ValueText == "MapGroup":
            {
                var inner = ResolveBuilderChain(model, member.Expression, cancellationToken, depth + 1);
                var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                var constant = argument is null
                    ? null
                    : model.GetConstantValue(argument, cancellationToken).Value as string;
                var notes = constant is null && argument is not null
                    ? inner.Notes.Append("MapGroup 접두사가 상수가 아니다.").ToArray()
                    : inner.Notes;
                return new BuilderChain(
                    EndpointIdentity.Append(inner.Prefix, constant ?? string.Empty),
                    inner.Complete,
                    notes);
            }

            case InvocationExpressionSyntax invocation:
            {
                // WithTags(...)처럼 접두사를 바꾸지 않는 체이닝만 통과시키고,
                // builder.Build()처럼 라우트 빌더가 아닌 수신 객체는 더 따라가지 않는다.
                return invocation.Expression is MemberAccessExpressionSyntax chained &&
                       IsEndpointRouteBuilder(model.GetTypeInfo(chained.Expression, cancellationToken).Type)
                    ? ResolveBuilderChain(model, chained.Expression, cancellationToken, depth + 1)
                    : new BuilderChain(string.Empty, true, []);
            }

            case IdentifierNameSyntax identifier:
            {
                var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
                if (symbol is IParameterSymbol)
                {
                    // 메서드 인자로 받은 빌더다. 접두사는 이 메서드를 부르는 쪽에서 채운다.
                    return new BuilderChain(string.Empty, false, []);
                }

                if (symbol is ILocalSymbol local && IsEndpointRouteBuilder(local.Type))
                {
                    var declaration = local.DeclaringSyntaxReferences.Length > 0
                        ? local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken)
                        : null;
                    if (declaration is VariableDeclaratorSyntax { Initializer.Value: { } initializer })
                    {
                        return ResolveBuilderChain(model, initializer, cancellationToken, depth + 1);
                    }
                }

                return new BuilderChain(string.Empty, true, []);
            }

            case MemberAccessExpressionSyntax member:
                return IsEndpointRouteBuilder(model.GetTypeInfo(member.Expression, cancellationToken).Type)
                    ? ResolveBuilderChain(model, member.Expression, cancellationToken, depth + 1)
                    : new BuilderChain(string.Empty, true, []);

            default:
                return new BuilderChain(string.Empty, true, []);
        }
    }

    private static IMethodSymbol? FindEnclosingMethod(
        SemanticModel model,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax method:
                    return model.GetDeclaredSymbol(method, cancellationToken) as IMethodSymbol;
                case LocalFunctionStatementSyntax local:
                    return model.GetDeclaredSymbol(local, cancellationToken) as IMethodSymbol;
                case CompilationUnitSyntax:
                    return null;
            }
        }

        return null;
    }

    private sealed record BuilderChain(string Prefix, bool Complete, IReadOnlyList<string> Notes);

    /// <param name="PrefixOwner">접두사를 아직 못 채운 등록을 감싼 메서드. null이면 접두사가 완성된 등록이다.</param>
    private sealed record RegistrarCall(
        IMethodSymbol Target,
        string Prefix,
        IMethodSymbol? PrefixOwner,
        IReadOnlyList<string> Notes);

    private sealed record Registration(
        string ProjectKey,
        string File,
        int Line,
        IReadOnlyList<string> HttpMethods,
        AnalysisCallable? Handler,
        HandlerKind HandlerKind,
        CallConfidence Confidence,
        IReadOnlyList<string> Notes)
    {
        public string LocalPrefix { get; init; } = string.Empty;

        /// <summary>이 등록을 감싼 <c>MapGroup</c> 접두사. 목록에서 묶는 기준이 된다.</summary>
        public string GroupPrefix { get; init; } = string.Empty;

        public IMethodSymbol? PrefixOwner { get; init; }

        public IReadOnlyList<RegistrarCall> RegistrarCalls { get; init; } = [];
    }
}

/// <param name="Notes">지원하지 못한 등록 패턴. 진입점 0개를 조용한 정상 결과로 넘기지 않기 위해 남긴다.</param>
public sealed record MinimalApiScanResult(IReadOnlyList<ScannedEntryPoint> EntryPoints, IReadOnlyList<string> Notes);

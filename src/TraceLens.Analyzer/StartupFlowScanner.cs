using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// Program.cs를 훑어 "시작 흐름"(DI 등록 + 미들웨어 파이프라인)을 만든다.
/// <c>builder.Services</c>/<c>app</c> 계열 호출만 골라 소스 순서대로 나열하고,
/// 솔루션 안에 정의된 확장 메서드(<c>AddFixtureServices()</c> 등)는 그 본문까지 따라 들어가 하위 단계로 펼친다.
/// </summary>
public sealed class StartupFlowScanner(ILogger<StartupFlowScanner> logger)
{
    /// <summary>확장 메서드를 따라 들어가는 최대 깊이. 순환·과도한 중첩을 막는다.</summary>
    private const int MaxDepth = 5;

    /// <summary>이보다 많으면 잘라내고 경고를 남긴다.</summary>
    private const int MaxSteps = 400;

    private enum Phase { Service, Middleware }

    public async Task<StartupFlowDocument?> ScanAsync(
        LoadedSolution loaded,
        TraceLensConfig config,
        string solutionDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!config.IncludeStartupFlow)
        {
            return null;
        }

        var programDocument = await FindProgramDocumentAsync(loaded, cancellationToken);
        if (programDocument is null)
        {
            logger.LogInformation("Program.cs를 찾지 못해 시작 흐름을 건너뛴다.");
            return null;
        }

        var root = await programDocument.GetSyntaxRootAsync(cancellationToken);
        var model = await programDocument.GetSemanticModelAsync(cancellationToken);
        if (root is null || model is null)
        {
            return null;
        }

        var context = new ScanContext(loaded, solutionDirectory);
        var serviceSteps = new List<StartupStep>();
        var middlewareSteps = new List<StartupStep>();

        foreach (var invocation in EnumerateInvocations(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var phase = ClassifyPhase(GetReceiverType(invocation, model, cancellationToken));
            if (phase is null)
            {
                continue;
            }

            var step = await BuildStepAsync(invocation, model, phase.Value, context, depth: 0, cancellationToken);
            if (step is null)
            {
                continue;
            }

            (phase.Value == Phase.Service ? serviceSteps : middlewareSteps).Add(step);
        }

        if (context.Truncated)
        {
            logger.LogWarning("시작 흐름 단계가 {Max}개를 넘어 일부를 잘랐다.", MaxSteps);
        }

        logger.LogInformation(
            "시작 흐름: 서비스 등록 {ServiceCount}개 · 미들웨어 {MiddlewareCount}개 ({File})",
            serviceSteps.Count,
            middlewareSteps.Count,
            programDocument.FilePath);

        return new StartupFlowDocument(
            SourcePaths.ToSolutionRelative(solutionDirectory, programDocument.FilePath) ?? programDocument.FilePath ?? "Program.cs",
            serviceSteps,
            middlewareSteps);
    }

    private async Task<StartupStep?> BuildStepAsync(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        Phase phase,
        ScanContext context,
        int depth,
        CancellationToken cancellationToken)
    {
        if (context.StepCount >= MaxSteps)
        {
            context.Truncated = true;
            return null;
        }

        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol symbol)
        {
            return null;
        }

        context.StepCount++;
        var file = SourcePaths.ToSolutionRelative(context.SolutionDirectory, invocation.SyntaxTree.FilePath)
            ?? invocation.SyntaxTree.FilePath ?? string.Empty;
        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var typeArguments = GetTypeArguments(invocation, model, cancellationToken);
        var lifetime = ClassifyLifetime(symbol.Name);

        // AddScoped<T>()/AddScoped<TService, TImpl>() 형태(제네릭 오버로드)만 등록으로 잡는다.
        // Type 인자를 받는 비제네릭 오버로드는 v1 범위 밖이라 설정 호출로 남는다.
        if (lifetime is not null && typeArguments.Count is 1 or 2)
        {
            return BuildRegistrationStep(typeArguments, lifetime.Value, file, line);
        }

        if (depth < MaxDepth && HasSolutionSource(symbol) && context.Visiting.Add(symbol))
        {
            try
            {
                var children = await ExpandAsync(symbol, phase, context, depth + 1, cancellationToken);
                if (children.Count > 0)
                {
                    return new StartupStep(
                        StartupStepKind.ExtensionCall,
                        FormatCall(symbol.Name, invocation),
                        null, null, null, null, null,
                        file, line, children);
                }
            }
            finally
            {
                context.Visiting.Remove(symbol);
            }
        }

        return new StartupStep(
            phase == Phase.Service ? StartupStepKind.Configuration : StartupStepKind.Middleware,
            FormatCall(symbol.Name, invocation),
            typeArguments.Count > 0 ? SymbolNames.GetClassDisplayName(typeArguments[0]) : null,
            typeArguments.Count > 0 && HasSolutionSource(typeArguments[0]) ? SymbolNames.GetClassId(typeArguments[0]) : null,
            null, null, null,
            file, line, []);
    }

    private static StartupStep BuildRegistrationStep(
        IReadOnlyList<INamedTypeSymbol> typeArguments, StartupLifetime lifetime, string file, int line)
    {
        var implementationType = typeArguments[^1];
        var serviceType = typeArguments.Count == 2 ? typeArguments[0] : null;
        var implementationName = SymbolNames.GetClassDisplayName(implementationType);
        var serviceName = serviceType is null ? null : SymbolNames.GetClassDisplayName(serviceType);
        return new StartupStep(
            StartupStepKind.Registration,
            serviceName is null ? implementationName : $"{serviceName} → {implementationName}",
            serviceName,
            serviceType is not null && HasSolutionSource(serviceType) ? SymbolNames.GetClassId(serviceType) : null,
            implementationName,
            HasSolutionSource(implementationType) ? SymbolNames.GetClassId(implementationType) : null,
            lifetime,
            file,
            line,
            []);
    }

    /// <summary>솔루션 안에 정의된 확장 메서드(<paramref name="method"/>)의 본문에서 같은 계열(서비스/미들웨어) 호출을 찾는다.</summary>
    private async Task<IReadOnlyList<StartupStep>> ExpandAsync(
        IMethodSymbol method, Phase phase, ScanContext context, int depth, CancellationToken cancellationToken)
    {
        var reference = method.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference is null)
        {
            return [];
        }

        var methodDocument = context.Loaded.Solution.GetDocument(reference.SyntaxTree);
        var methodModel = methodDocument is null ? null : await methodDocument.GetSemanticModelAsync(cancellationToken);
        if (methodModel is null)
        {
            return [];
        }

        var declaration = await reference.GetSyntaxAsync(cancellationToken);
        var children = new List<StartupStep>();
        foreach (var invocation in EnumerateInvocations(declaration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var invocationPhase = ClassifyPhase(GetReceiverType(invocation, methodModel, cancellationToken));
            if (invocationPhase != phase)
            {
                continue;
            }

            var step = await BuildStepAsync(invocation, methodModel, phase, context, depth, cancellationToken);
            if (step is not null)
            {
                children.Add(step);
            }
        }

        return children;
    }

    private static async Task<Document?> FindProgramDocumentAsync(LoadedSolution loaded, CancellationToken cancellationToken)
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

    private static IEnumerable<InvocationExpressionSyntax> EnumerateInvocations(SyntaxNode root) => root
        .DescendantNodesAndSelf()
        .OfType<InvocationExpressionSyntax>()
        .OrderBy(GetOrderKey);

    /// <summary>
    /// 보통은 SpanStart로 충분하지만, <c>services.AddScoped&lt;A&gt;().AddScoped&lt;B&gt;()</c>처럼 체이닝한 호출은
    /// 바깥쪽(나중에 읽히는 <c>B</c>) 호출도 SpanStart가 맨 앞의 <c>services</c>와 같아서 구분이 안 된다.
    /// 그래서 메서드 이름 토큰의 위치로 정렬해 코드에서 눈으로 읽는 순서(A → B)를 그대로 따른다.
    /// </summary>
    private static int GetOrderKey(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Name.SpanStart
            : invocation.SpanStart;

    private static ITypeSymbol? GetReceiverType(
        InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        // "WebApplication.CreateBuilder(args)"처럼 타입 자체를 거쳐 부르는 정적 메서드는 인스턴스 리시버가 없다.
        // GetTypeInfo는 이런 타입 한정자에도 그 타입 자신을 돌려줄 때가 있어(WebApplication이 IApplicationBuilder를 구현하므로
        // 오탐이 났다), 먼저 심볼 종류로 걸러낸다.
        var qualifierSymbol = model.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
        if (qualifierSymbol is INamedTypeSymbol or INamespaceSymbol)
        {
            return null;
        }

        return model.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
    }

    private static Phase? ClassifyPhase(ITypeSymbol? receiverType)
    {
        if (receiverType is null)
        {
            return null;
        }

        if (Implements(receiverType, "Microsoft.Extensions.DependencyInjection", "IServiceCollection"))
        {
            return Phase.Service;
        }

        // WebApplication은 IApplicationBuilder·IEndpointRouteBuilder를 모두 구현하고,
        // app.MapGroup(...)이 돌려주는 RouteGroupBuilder는 IEndpointRouteBuilder만 구현한다.
        if (Implements(receiverType, "Microsoft.AspNetCore.Builder", "IApplicationBuilder") ||
            Implements(receiverType, "Microsoft.AspNetCore.Routing", "IEndpointRouteBuilder"))
        {
            return Phase.Middleware;
        }

        return null;
    }

    private static bool Implements(ITypeSymbol type, string ns, string name)
    {
        if (IsType(type, ns, name))
        {
            return true;
        }

        return type is INamedTypeSymbol named && named.AllInterfaces.Any(item => IsType(item, ns, name));
    }

    private static bool IsType(ITypeSymbol type, string ns, string name) =>
        type.Name == name && type.ContainingNamespace?.ToDisplayString() == ns;

    /// <summary><c>AddScoped</c>/<c>TryAddSingleton</c>/<c>AddKeyedTransient</c> 같은 이름에서 수명을 뽑는다. 등록 메서드가 아니면 null.</summary>
    private static StartupLifetime? ClassifyLifetime(string methodName)
    {
        if (!methodName.StartsWith("Add", StringComparison.Ordinal) && !methodName.StartsWith("TryAdd", StringComparison.Ordinal))
        {
            return null;
        }

        if (methodName.Contains("Scoped", StringComparison.Ordinal)) return StartupLifetime.Scoped;
        if (methodName.Contains("Singleton", StringComparison.Ordinal)) return StartupLifetime.Singleton;
        if (methodName.Contains("Transient", StringComparison.Ordinal)) return StartupLifetime.Transient;
        return null;
    }

    /// <summary>호출식의 제네릭 타입 인자를 해석한다(<c>a.b.M&lt;T1, T2&gt;()</c>의 <c>T1</c>/<c>T2</c>).</summary>
    private static IReadOnlyList<INamedTypeSymbol> GetTypeArguments(
        InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name as GenericNameSyntax,
            GenericNameSyntax generic => generic,
            _ => null
        };
        if (name is null)
        {
            return [];
        }

        return name.TypeArgumentList.Arguments
            .Select(argument => model.GetTypeInfo(argument, cancellationToken).Type as INamedTypeSymbol)
            .Where(type => type is not null)
            .Select(type => type!)
            .ToArray();
    }

    /// <summary>문자열 리터럴 인자만 보여준다(람다·옵션 객체 등은 코드 그대로 옮기면 알아보기 어려워 뺀다).</summary>
    private static string FormatCall(string methodName, InvocationExpressionSyntax invocation)
    {
        var arguments = invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Select(literal => literal.ToString());
        return $"{methodName}({string.Join(", ", arguments)})";
    }

    private static bool HasSolutionSource(ISymbol symbol) => symbol.DeclaringSyntaxReferences.Length > 0;

    private sealed class ScanContext(LoadedSolution loaded, string solutionDirectory)
    {
        public LoadedSolution Loaded { get; } = loaded;
        public string SolutionDirectory { get; } = solutionDirectory;
        public int StepCount { get; set; }
        public bool Truncated { get; set; }
        public HashSet<IMethodSymbol> Visiting { get; } = new(SymbolEqualityComparer.Default);
    }
}

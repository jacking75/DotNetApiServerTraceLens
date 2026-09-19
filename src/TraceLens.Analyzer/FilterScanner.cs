using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// MVC 필터와 <c>[Authorize]</c> 정책을 모은다(T71).
///
/// 요청이 컨트롤러에 닿기 전에 **필터**가 무엇을 하는지는 실행 순서 보기의 가장 큰 빈칸이었다.
/// 필터는 미들웨어가 아니므로 파이프라인 단계와 섞지 않고 별도 절로 낸다 —
/// 미들웨어를 다 지난 뒤 엔드포인트 실행 시점에 도는 것이라 순서가 다르다.
///
/// 이 스캐너는 호출 흐름 엣지를 늘리지 않는다(CLAUDE.md 제약 2의 "미들웨어 보기" 예외 안이다).
/// 상속·인터페이스로 판정하고 확정하지 못하면 추정으로 남긴다.
/// </summary>
public sealed class FilterScanner(ILogger<FilterScanner> logger)
{
    /// <summary>필터로 보는 인터페이스. 하나라도 구현하면 필터다.</summary>
    private static readonly string[] FilterInterfaces =
    [
        "IActionFilter", "IAsyncActionFilter",
        "IResultFilter", "IAsyncResultFilter",
        "IExceptionFilter", "IAsyncExceptionFilter",
        "IAuthorizationFilter", "IAsyncAuthorizationFilter",
        "IResourceFilter", "IAsyncResourceFilter",
        "IPageFilter", "IAsyncPageFilter"
    ];

    /// <summary>필터를 전역 등록하는 자리. <c>o.Filters.Add&lt;T&gt;()</c>·<c>o.Filters.Add(typeof(T))</c>.</summary>
    private const string GlobalFilterCollection = "Filters";

    public async Task<IReadOnlyList<MvcFilterInfo>> ScanAsync(
        LoadedSolution loaded,
        string solutionDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new List<MvcFilterInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in loaded.Solution.Projects.Where(item => loaded.AnalyzableProjectIds.Contains(item.Id)))
        {
            foreach (var document in project.Documents.Where(item => item.SourceCodeKind == SourceCodeKind.Regular))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = SourcePaths.ToSolutionRelative(solutionDirectory, document.FilePath);
                if (relativePath is null || SourcePaths.IsBuildOutput(relativePath))
                {
                    continue;
                }

                var semantics = await loaded.GetSemanticsAsync(document, cancellationToken);
                if (semantics is null)
                {
                    continue;
                }

                var (root, model) = (semantics.Root, semantics.Model);
                CollectGlobalFilters(root, model, relativePath, solutionDirectory, result, seen, cancellationToken);
                CollectAttributeFilters(root, model, relativePath, solutionDirectory, result, seen, cancellationToken);
            }
        }

        logger.LogInformation(
            "MVC 필터 {Count}개를 찾았다(전역 {Global}개).",
            result.Count,
            result.Count(item => item.Scope == MvcFilterScope.Global));
        return result;
    }

    /// <summary><c>AddControllers(o =&gt; o.Filters.Add&lt;T&gt;())</c> 형태의 전역 등록.</summary>
    private void CollectGlobalFilters(
        SyntaxNode root,
        SemanticModel model,
        string relativePath,
        string solutionDirectory,
        List<MvcFilterInfo> result,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (invocation.Expression is not MemberAccessExpressionSyntax member ||
                member.Name.Identifier.ValueText != "Add" ||
                member.Expression is not MemberAccessExpressionSyntax collection ||
                collection.Name.Identifier.ValueText != GlobalFilterCollection)
            {
                continue;
            }

            var type = ReadTypeArgument(member.Name, invocation, model, cancellationToken);
            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (type is null)
            {
                // 타입을 확정하지 못했다. 등록이 있다는 사실은 남긴다 — 조용히 버리면 "필터가 없다"로 읽힌다.
                Append(result, seen, new MvcFilterInfo(
                    $"filter:{relativePath}:{line}",
                    invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() ?? "(알 수 없음)",
                    MvcFilterScope.Global,
                    [],
                    null,
                    string.Empty,
                    relativePath,
                    line,
                    null,
                    0,
                    null,
                    null,
                    null,
                    CallConfidence.Estimated,
                    "등록한 필터 타입을 상수로 확정하지 못했다."));
                continue;
            }

            Append(result, seen, BuildFilter(
                type, MvcFilterScope.Global, string.Empty, relativePath, line, solutionDirectory, model, ReadOrder(invocation)));
        }
    }

    /// <summary>컨트롤러·액션에 붙은 필터 특성(<c>[ServiceFilter]</c>·<c>[TypeFilter]</c>·필터 특성 파생).</summary>
    private void CollectAttributeFilters(
        SyntaxNode root,
        SemanticModel model,
        string relativePath,
        string solutionDirectory,
        List<MvcFilterInfo> result,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        foreach (var typeSyntax in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model.GetDeclaredSymbol(typeSyntax, cancellationToken) is not INamedTypeSymbol declared)
            {
                continue;
            }

            foreach (var (attribute, filterType, order) in ReadFilterAttributes(declared, model, cancellationToken))
            {
                var line = AttributeLine(attribute, typeSyntax);
                Append(result, seen, BuildFilter(
                    filterType, MvcFilterScope.Controller, declared.Name, relativePath, line,
                    solutionDirectory, model, order));
            }

            foreach (var methodSyntax in typeSyntax.Members.OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(methodSyntax, cancellationToken) is not IMethodSymbol method)
                {
                    continue;
                }

                foreach (var (attribute, filterType, order) in ReadFilterAttributes(method, model, cancellationToken))
                {
                    var line = AttributeLine(attribute, methodSyntax);
                    Append(result, seen, BuildFilter(
                        filterType, MvcFilterScope.Action, $"{declared.Name}.{method.Name}", relativePath, line,
                        solutionDirectory, model, order));
                }
            }
        }
    }

    /// <summary>
    /// 이 심볼에 붙은 필터 특성들.
    /// <c>[ServiceFilter(typeof(T))]</c>·<c>[TypeFilter(typeof(T))]</c>는 인자에서 타입을 읽고,
    /// 필터 인터페이스를 직접 구현한 특성은 그 특성 자체가 필터다.
    /// </summary>
    private static IEnumerable<(AttributeData Attribute, INamedTypeSymbol Type, int? Order)> ReadFilterAttributes(
        ISymbol symbol, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributeName = TrimAttributeSuffix(attribute.AttributeClass?.Name);
            var order = ReadOrderArgument(attribute);

            if (attributeName is "ServiceFilter" or "TypeFilter")
            {
                if (attribute.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol type)
                {
                    yield return (attribute, type, order);
                }

                continue;
            }

            if (attribute.AttributeClass is { } declaring && ImplementsFilterInterface(declaring))
            {
                yield return (attribute, declaring, order);
            }
        }
    }

    private static bool ImplementsFilterInterface(INamedTypeSymbol type) =>
        type.AllInterfaces.Any(item => FilterInterfaces.Contains(item.Name, StringComparer.Ordinal));

    /// <summary>필터 하나를 결과 항목으로 만든다. 본문(요청·응답 구간·단락)은 구현 타입에서 읽는다.</summary>
    private static MvcFilterInfo BuildFilter(
        INamedTypeSymbol type,
        MvcFilterScope scope,
        string appliesTo,
        string registrationFile,
        int registrationLine,
        string solutionDirectory,
        SemanticModel model,
        int? order)
    {
        var kinds = type.AllInterfaces
            .Select(item => item.Name)
            .Where(name => FilterInterfaces.Contains(name, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var declaration = type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var implementationFile = declaration is null
            ? null
            : SourcePaths.ToSolutionRelative(solutionDirectory, declaration.SyntaxTree.FilePath);
        var implementationLine = declaration?.GetLocation().GetLineSpan().StartLinePosition.Line + 1 ?? 0;

        var body = ReadFilterBody(type, declaration);
        return new MvcFilterInfo(
            $"filter:{type.ToDisplayString()}:{scope}:{appliesTo}",
            type.Name,
            scope,
            kinds,
            order,
            appliesTo,
            registrationFile,
            registrationLine,
            implementationFile,
            implementationLine,
            body.Pre,
            body.Post,
            body.ShortCircuit,
            kinds.Length > 0 ? CallConfidence.Certain : CallConfidence.Estimated,
            kinds.Length > 0 ? null : "필터 인터페이스를 확정하지 못했다. 이름·등록 형태로만 필터로 봤다.");
    }

    private sealed record FilterBody(string? Pre, string? Post, MiddlewareShortCircuit? ShortCircuit);

    /// <summary>
    /// 필터 본문에서 요청·응답 구간과 단락 지점을 읽는다.
    /// 단락은 <c>context.Result = …</c> 대입이다 — 그 자리에서 파이프라인이 끊기고 액션이 돌지 않는다.
    /// </summary>
    private static FilterBody ReadFilterBody(INamedTypeSymbol type, SyntaxNode? declaration)
    {
        if (declaration is not TypeDeclarationSyntax typeDeclaration)
        {
            return new FilterBody(null, null, null);
        }

        var methods = typeDeclaration.Members.OfType<MethodDeclarationSyntax>().ToArray();
        var executing = methods.FirstOrDefault(method =>
            method.Identifier.ValueText is "OnActionExecuting" or "OnResourceExecuting" or
                "OnAuthorization" or "OnResultExecuting" or "OnActionExecutionAsync");
        var executed = methods.FirstOrDefault(method =>
            method.Identifier.ValueText is "OnActionExecuted" or "OnResourceExecuted" or
                "OnResultExecuted" or "OnException");

        MiddlewareShortCircuit? shortCircuit = null;
        foreach (var assignment in typeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is not MemberAccessExpressionSyntax member ||
                member.Name.Identifier.ValueText != "Result")
            {
                continue;
            }

            var line = assignment.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var condition = assignment.FirstAncestorOrSelf<IfStatementSyntax>()?.Condition.ToString() ?? string.Empty;
            shortCircuit = new MiddlewareShortCircuit(ReadStatusCode(assignment), condition, line);
            break;
        }

        return new FilterBody(
            executing is null ? null : $"{executing.Identifier.ValueText}에서 요청을 본다",
            executed is null ? null : $"{executed.Identifier.ValueText}에서 결과를 본다",
            shortCircuit);
    }

    /// <summary>대입식에서 상태 코드를 읽는다. 확정하지 못하면 코드 조각을 그대로 남긴다.</summary>
    private static string ReadStatusCode(AssignmentExpressionSyntax assignment)
    {
        var text = assignment.Right.ToString();
        foreach (var known in new[] { "Unauthorized", "Forbid", "BadRequest", "NotFound", "Conflict" })
        {
            if (text.Contains(known, StringComparison.Ordinal))
            {
                return known;
            }
        }

        var digits = new string(text.Where(char.IsDigit).ToArray());
        return digits.Length == 3 ? digits : text;
    }

    private static INamedTypeSymbol? ReadTypeArgument(
        SimpleNameSyntax name,
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (name is GenericNameSyntax { TypeArgumentList.Arguments.Count: > 0 } generic)
        {
            return model.GetTypeInfo(generic.TypeArgumentList.Arguments[0], cancellationToken).Type as INamedTypeSymbol;
        }

        return invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax typeOf
            ? model.GetTypeInfo(typeOf.Type, cancellationToken).Type as INamedTypeSymbol
            : null;
    }

    /// <summary><c>o.Filters.Add&lt;T&gt;(order)</c>의 순서 인자.</summary>
    private static int? ReadOrder(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Select(literal => int.TryParse(literal.Token.ValueText, out var value) ? value : (int?)null)
            .FirstOrDefault(value => value is not null);

    private static int? ReadOrderArgument(AttributeData attribute) => attribute.NamedArguments
        .Where(item => string.Equals(item.Key, "Order", StringComparison.Ordinal))
        .Select(item => item.Value.Value as int?)
        .FirstOrDefault();

    private static int AttributeLine(AttributeData attribute, SyntaxNode fallback) =>
        (attribute.ApplicationSyntaxReference?.GetSyntax() ?? fallback)
            .GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static void Append(List<MvcFilterInfo> result, HashSet<string> seen, MvcFilterInfo filter)
    {
        if (seen.Add(filter.Id))
        {
            result.Add(filter);
        }
    }

    private static string TrimAttributeSuffix(string? name) =>
        name?.EndsWith("Attribute", StringComparison.Ordinal) == true ? name[..^"Attribute".Length] : name ?? string.Empty;
}

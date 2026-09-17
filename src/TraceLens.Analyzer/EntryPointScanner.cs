using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// 컨트롤러의 <c>[HttpGet]</c> 계열 특성과 Minimal API 등록식에서 HTTP 진입점을 모은다.
/// 같은 메서드에 여러 동사·라우트가 걸릴 수 있으므로 노드 ID가 아니라 엔드포인트 ID로 중복을 제거한다.
/// </summary>
public sealed class EntryPointScanner(
    MinimalApiEntryPointScanner minimalApiScanner,
    ILogger<EntryPointScanner> logger)
{
    public async Task<EntryPointScanResult> ScanAsync(
        LoadedSolution loaded,
        TraceLensConfig config,
        string solutionDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ScannedEntryPoint>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

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

                var semantics = await loaded.GetSemanticsAsync(document, cancellationToken);
                if (semantics is null)
                {
                    continue;
                }

                var (root, model) = (semantics.Root, semantics.Model);

                foreach (var methodSyntax in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(methodSyntax, cancellationToken) is not IMethodSymbol method)
                    {
                        continue;
                    }

                    var callable = AnalysisCallable.ForMethod(method, methodSyntax, projectKey);
                    var line = methodSyntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                    // [NonAction]이 붙은 메서드는 라우팅 대상이 아니다.
                    if (method.GetAttributes().Any(item => TrimAttributeSuffix(item.AttributeClass?.Name) == "NonAction"))
                    {
                        continue;
                    }

                    foreach (var attribute in method.GetAttributes())
                    {
                        var attributeName = TrimAttributeSuffix(attribute.AttributeClass?.Name);
                        var acceptVerbs = attributeName == "AcceptVerbs";
                        if (!acceptVerbs &&
                            !config.EntryPointAttributes.Contains(attributeName, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // 같은 메서드에 [HttpGet("a")] [HttpGet("b")]가 붙으면 라우트마다 엔드포인트가 하나씩 나온다.
                        // 클래스 [Route]도 여러 개일 수 있고 기반 클래스에 있을 수도 있다. 그 곱만큼 라우트가 나온다.
                        // [AcceptVerbs("GET", "POST")]는 동사가 여러 개고 라우트는 명명 인자(Route)로 온다.
                        var methodRoute = acceptVerbs ? ReadNamedRoute(attribute) : ReadRoute(attribute);
                        var httpMethods = acceptVerbs ? ReadVerbs(attribute) : [ToHttpMethod(attributeName)];
                        foreach (var httpMethod in httpMethods)
                        foreach (var classRoute in GetClassRoutes(method.ContainingType))
                        {
                            var route = CombineRoute(classRoute, methodRoute, method);
                            var normalized = EndpointIdentity.NormalizeRoute(route);
                            var endpointId = EndpointIdentity.Disambiguate(
                                EndpointIdentity.Create(projectKey, httpMethod, normalized, callable.StableKey),
                                usedIds);
                            result.Add(new ScannedEntryPoint(
                                callable,
                                new EntryPoint(callable.NodeId, httpMethod, route)
                                {
                                    EndpointId = endpointId,
                                    NormalizedRoute = normalized,
                                    Source = EndpointSource.Controller,
                                    HandlerKind = HandlerKind.Method,
                                    Confidence = CallConfidence.Certain,
                                    RegistrationFile = relativePath,
                                    RegistrationLine = line,
                                    HandlerDisplayName = callable.DisplayName,
                                    ProjectKey = projectKey
                                }));
                        }
                    }
                }
            }
        }

        var minimalApi = await minimalApiScanner.ScanAsync(loaded, config, solutionDirectory, cancellationToken);
        foreach (var entryPoint in minimalApi.EntryPoints)
        {
            // 컨트롤러와 Minimal API가 같은 엔드포인트 ID를 만들 일은 없지만, 안전하게 한 번 더 확인한다.
            var endpointId = EndpointIdentity.Disambiguate(entryPoint.EntryPoint.EndpointId, usedIds);
            result.Add(endpointId == entryPoint.EntryPoint.EndpointId
                ? entryPoint
                : entryPoint with { EntryPoint = entryPoint.EntryPoint with { EndpointId = endpointId } });
        }

        var notes = new List<string>(minimalApi.Notes);
        if (result.Count == 0)
        {
            notes.Add(
                "HTTP 진입점을 하나도 찾지 못했다. 컨트롤러 특성(entryPointAttributes)과 Minimal API 등록 형태를 확인해야 한다. " +
                "이 결과를 \"진입점이 없는 정상 상태\"로 보면 안 된다.");
        }

        logger.LogInformation(
            "진입점 {Count}개를 찾았다(컨트롤러 {ControllerCount}개, Minimal API {MinimalCount}개).",
            result.Count,
            result.Count(item => item.EntryPoint.Source == EndpointSource.Controller),
            result.Count(item => item.EntryPoint.Source == EndpointSource.MinimalApi));
        return new EntryPointScanResult(result, notes);
    }

    private static string? ReadRoute(AttributeData attribute) =>
        attribute.ConstructorArguments.FirstOrDefault().Value as string;

    /// <summary><c>[AcceptVerbs("GET", "POST", Route = "x")]</c>의 Route 명명 인자.</summary>
    private static string? ReadNamedRoute(AttributeData attribute) => attribute.NamedArguments
        .Where(item => string.Equals(item.Key, "Route", StringComparison.Ordinal))
        .Select(item => item.Value.Value as string)
        .FirstOrDefault();

    /// <summary><c>[AcceptVerbs("GET", "POST")]</c>의 동사들. 하나도 못 읽으면 GET으로 본다.</summary>
    private static IReadOnlyList<string> ReadVerbs(AttributeData attribute)
    {
        var verbs = attribute.ConstructorArguments
            .SelectMany(argument => argument.Kind == TypedConstantKind.Array
                ? argument.Values.Select(item => item.Value as string)
                : [argument.Value as string])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return verbs.Length > 0 ? verbs : ["GET"];
    }

    /// <summary>
    /// 클래스 라우트 목록. 기반 클래스까지 올라가며 찾고, <c>[Route]</c>가 여러 개면 모두 돌려준다.
    /// 하나도 없으면 null 하나(= 접두사 없음)를 돌려준다.
    /// </summary>
    private static IReadOnlyList<string?> GetClassRoutes(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var routes = current.GetAttributes()
                .Where(item => TrimAttributeSuffix(item.AttributeClass?.Name) == "Route")
                .Select(ReadRoute)
                .Where(route => route is not null)
                .ToArray();
            if (routes.Length > 0)
            {
                return routes;
            }
        }

        return [null];
    }

    /// <summary>
    /// 클래스 라우트와 메서드 라우트를 잇는다.
    /// MVC 규칙대로 메서드 라우트가 <c>/</c>로 시작하면 <b>절대 경로</b>라 클래스 접두사를 붙이지 않는다
    /// (<c>[Route("api/[controller]")]</c> + <c>[HttpGet("/health")]</c>는 <c>/health</c>다).
    /// </summary>
    private static string CombineRoute(string? classRoute, string? methodRoute, IMethodSymbol method)
    {
        var combined = EndpointIdentity.Join(classRoute, methodRoute).TrimStart('/');
        combined = combined
            .Replace("[controller]", RemoveControllerSuffix(method.ContainingType.Name), StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", method.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("[area]", ReadAreaName(method.ContainingType), StringComparison.OrdinalIgnoreCase);
        return "/" + combined;
    }

    /// <summary><c>[Area("Admin")]</c>의 값. 없으면 빈 문자열이다.</summary>
    private static string ReadAreaName(INamedTypeSymbol type) => type.GetAttributes()
        .Where(item => TrimAttributeSuffix(item.AttributeClass?.Name) == "Area")
        .Select(ReadRoute)
        .FirstOrDefault(value => value is not null) ?? string.Empty;

    private static string RemoveControllerSuffix(string name) =>
        name.EndsWith("Controller", StringComparison.Ordinal) ? name[..^"Controller".Length] : name;

    private static string TrimAttributeSuffix(string? name) =>
        name?.EndsWith("Attribute", StringComparison.Ordinal) == true ? name[..^"Attribute".Length] : name ?? string.Empty;

    private static string ToHttpMethod(string attributeName) =>
        attributeName.StartsWith("Http", StringComparison.OrdinalIgnoreCase)
            ? attributeName[4..].ToUpperInvariant()
            : attributeName.ToUpperInvariant();
}

/// <param name="Handler">
/// 진입점의 실행 본문. 컨트롤러는 메서드, Minimal API는 메서드 그룹·람다·로컬 함수일 수 있다.
/// </param>
public sealed record ScannedEntryPoint(AnalysisCallable Handler, EntryPoint EntryPoint);

/// <param name="Notes">지원하지 못한 등록 패턴과 "진입점 0개"에 대한 안내.</param>
public sealed record EntryPointScanResult(IReadOnlyList<ScannedEntryPoint> EntryPoints, IReadOnlyList<string> Notes);

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
    EntryPointCandidateScanner candidateScanner,
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
        // 기반 클래스에 선언한 액션은 파생 컨트롤러마다 라우트가 하나씩 생긴다.
        // 파생 타입은 다른 파일에 있을 수 있어 먼저 모아 두고, 본 순회가 끝난 뒤에 풀어 준다.
        var concreteTypes = new List<INamedTypeSymbol>();
        var pending = new List<PendingBaseAction>();

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

                foreach (var typeSyntax in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(typeSyntax, cancellationToken) is INamedTypeSymbol { IsAbstract: false } declared)
                    {
                        concreteTypes.Add(declared);
                    }
                }

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
                        //
                        // 메서드에 붙은 [Route]도 함께 읽는다. ASP.NET Core에서 진입 특성의 템플릿과 메서드 [Route]는
                        // 서로를 대체하지 않고 **각각 별도 라우트**가 된다 — `[Route("items")] [HttpGet]`은
                        // 클래스 접두사 + "items"이지 클래스 접두사 하나가 아니다. 예전에는 메서드 [Route]를 읽지 않아
                        // 존재하지 않는 라우트가 "확실"로 나왔다(T54).
                        var attributeRoute = acceptVerbs ? ReadNamedRoute(attribute) : ReadRoute(attribute);
                        var methodRoutes = CombineMethodRoutes(attributeRoute, ReadMethodRoutes(method));
                        var httpMethods = acceptVerbs ? ReadVerbs(attribute) : [ToHttpMethod(attributeName)];
                        var hidden = IsHiddenFromApiExplorer(method);
                        var authorization = ReadAuthorization(method, relativePath, line);

                        // 기반 클래스(abstract)에 선언한 액션은 그 클래스 이름으로 라우트를 만들면
                        // 존재하지 않는 경로가 "확실"로 나온다. 파생 컨트롤러를 찾은 뒤에 만든다.
                        //
                        // 비추상 기반 컨트롤러(`class CrudController<T> : ControllerBase`)도 파생마다 라우트가 생긴다.
                        // 그 기반 클래스 자체가 라우팅 가능한 컨트롤러이면 기반 라우트도 실제로 존재하므로 둘 다 남긴다.
                        foreach (var entry in BuildControllerEntryPoints(
                            callable, method, method.ContainingType, methodRoutes, httpMethods,
                            projectKey, relativePath, line, usedIds, notes: null, hidden, authorization,
                            skipSelf: method.ContainingType.IsAbstract))
                        {
                            result.Add(entry);
                        }

                        pending.Add(new PendingBaseAction(
                            callable, method, methodRoutes, httpMethods, projectKey, relativePath, line,
                            hidden, authorization));
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

        var baseActionNotes = new List<string>();
        foreach (var item in pending)
        {
            var derived = concreteTypes
                .Where(type => InheritsFrom(type, item.Method.ContainingType))
                .DistinctBy(type => type.ToDisplayString(), StringComparer.Ordinal)
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToArray();
            if (derived.Length == 0)
            {
                if (item.Method.ContainingType.IsAbstract)
                {
                    baseActionNotes.Add(
                        $"`{item.Method.ContainingType.Name}.{item.Method.Name}`은 기반 클래스 선언이라 파생 라우트를 확정하지 못했다. " +
                        "솔루션 안에서 이 클래스를 상속한 컨트롤러를 찾지 못했다.");
                }

                continue;
            }

            foreach (var type in derived)
            {
                foreach (var entry in BuildControllerEntryPoints(
                    item.Callable, item.Method, type, item.MethodRoutes, item.HttpMethods,
                    item.ProjectKey, item.RelativePath, item.Line, usedIds, baseActionNotes,
                    item.Hidden, item.Authorization, skipSelf: false))
                {
                    result.Add(entry);
                }
            }
        }

        // HTTP 밖 실행 단위는 세기만 한다. 진입점·엣지를 만들지 않는다.
        var candidates = await candidateScanner.ScanAsync(loaded, solutionDirectory, cancellationToken);

        var notes = new List<string>(minimalApi.Notes);
        notes.AddRange(baseActionNotes.Distinct(StringComparer.Ordinal));
        foreach (var group in candidates)
        {
            var names = group.Samples.Count > 0 ? $"({string.Join(", ", group.Samples)}) " : string.Empty;
            notes.Add(
                $"`{group.Kind}` {group.Count}개{names}는 HTTP 진입점이 아니라 호출 흐름의 출발점으로 잡지 않았다. " +
                "변경 영향의 \"도달하는 진입점 없음\"은 이들을 세지 않은 결과다.");
        }

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
        return new EntryPointScanResult(result, notes) { Candidates = candidates };
    }

    /// <summary>
    /// 기반 클래스에 선언돼 파생 컨트롤러를 찾은 뒤에야 라우트를 확정할 수 있는 액션.
    /// 추상 기반뿐 아니라 <b>비추상</b> 기반 컨트롤러도 여기 들어온다(파생마다 라우트가 하나씩 더 생긴다).
    /// </summary>
    private sealed record PendingBaseAction(
        AnalysisCallable Callable,
        IMethodSymbol Method,
        IReadOnlyList<string?> MethodRoutes,
        IReadOnlyList<string> HttpMethods,
        string ProjectKey,
        string RelativePath,
        int Line,
        bool Hidden,
        EndpointAuthorization? Authorization);

    /// <summary>
    /// 액션 하나에서 진입점을 만든다. 클래스 라우트·<c>[controller]</c> 치환은 <paramref name="declaringType"/> 기준이다.
    /// 기반 클래스에 선언한 액션이면 여기에 <b>파생 타입</b>이 들어와 존재하지 않는 라우트를 만들지 않는다.
    ///
    /// <c>[ApiVersion]</c>이 있고 라우트에 <c>{version:apiVersion}</c> 토큰이 있으면 버전마다 하나씩 만든다.
    /// 토큰이 없으면(헤더·쿼리 버전) 라우트는 그대로 두고 endpointId에만 버전을 넣어 구분한다.
    /// </summary>
    private static IEnumerable<ScannedEntryPoint> BuildControllerEntryPoints(
        AnalysisCallable callable,
        IMethodSymbol method,
        INamedTypeSymbol declaringType,
        IReadOnlyList<string?> methodRoutes,
        IReadOnlyList<string> httpMethods,
        string projectKey,
        string relativePath,
        int line,
        HashSet<string> usedIds,
        List<string>? notes,
        bool hiddenFromApiExplorer,
        EndpointAuthorization? authorization,
        bool skipSelf)
    {
        // 추상 기반 클래스 자신은 라우팅 대상이 아니다. 그 이름으로 라우트를 만들면 존재하지 않는 경로가 나온다.
        if (skipSelf)
        {
            yield break;
        }

        var versions = ReadApiVersions(method, declaringType);
        var isBaseDeclared = !SymbolEqualityComparer.Default.Equals(method.ContainingType, declaringType);
        var typeAuthorization = ReadAuthorization(declaringType, relativePath, line);
        foreach (var httpMethod in httpMethods)
        foreach (var classRoute in GetClassRoutes(declaringType))
        foreach (var methodRoute in methodRoutes)
        {
            var template = CombineRoute(classRoute, methodRoute, declaringType, method);
            foreach (var (route, version) in ExpandVersions(template, versions))
            {
                var normalized = EndpointIdentity.NormalizeRoute(route);
                // 같은 기반 메서드가 파생마다 나오므로 등록 식별자에 파생 타입과 버전을 함께 넣는다.
                var identity = version is null
                    ? $"{callable.StableKey}|{declaringType.ToDisplayString()}"
                    : $"{callable.StableKey}|{declaringType.ToDisplayString()}|v{version}";
                var endpointId = EndpointIdentity.Disambiguate(
                    EndpointIdentity.Create(projectKey, httpMethod, normalized, identity),
                    usedIds);
                var entryNotes = new List<string>();
                if (isBaseDeclared)
                {
                    entryNotes.Add($"`{method.ContainingType.Name}`에 선언한 액션을 `{declaringType.Name}`이(가) 상속했다.");
                }

                yield return new ScannedEntryPoint(
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
                        ProjectKey = projectKey,
                        Notes = entryNotes,
                        ApiVersions = versions,
                        HiddenFromApiExplorer = hiddenFromApiExplorer || IsHiddenFromApiExplorer(declaringType),
                        // 메서드 쪽 인가가 있으면 그것이 이긴다. 없으면 컨트롤러 쪽을 쓴다.
                        Authorization = authorization ?? typeAuthorization
                    });
            }
        }

        if (notes is not null && versions.Count > 1)
        {
            notes.Add($"`{declaringType.Name}`은 API 버전 {string.Join(", ", versions)}을(를) 선언했다.");
        }
    }

    /// <summary>
    /// <c>{version:apiVersion}</c> 토큰을 버전마다 치환한다.
    /// 토큰이 없으면 라우트는 그대로 두고 버전만 돌려준다(헤더·쿼리 버전이라 경로가 같다).
    /// </summary>
    private static IEnumerable<(string Route, string? Version)> ExpandVersions(
        string template, IReadOnlyList<string> versions)
    {
        if (versions.Count == 0)
        {
            yield return (template, null);
            yield break;
        }

        var hasToken = VersionToken.IsMatch(template);
        foreach (var version in versions)
        {
            yield return hasToken
                ? (VersionToken.Replace(template, version), version)
                : (template, version);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex VersionToken = new(
        @"\{version(:apiVersion)?\}",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary><c>[ApiVersion("1.0")]</c>을 클래스·메서드에서 읽는다. 메서드 쪽이 있으면 그것이 이긴다.</summary>
    private static IReadOnlyList<string> ReadApiVersions(IMethodSymbol method, INamedTypeSymbol declaringType)
    {
        var fromMethod = ReadApiVersionAttributes(method.GetAttributes());
        if (fromMethod.Count > 0)
        {
            return fromMethod;
        }

        for (var type = declaringType; type is not null; type = type.BaseType)
        {
            var fromType = ReadApiVersionAttributes(type.GetAttributes());
            if (fromType.Count > 0)
            {
                return fromType;
            }
        }

        return [];
    }

    private static IReadOnlyList<string> ReadApiVersionAttributes(IEnumerable<AttributeData> attributes) => attributes
        .Where(item => TrimAttributeSuffix(item.AttributeClass?.Name) == "ApiVersion")
        .Select(item => item.ConstructorArguments.FirstOrDefault().Value switch
        {
            string text => text,
            double number => number.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        })
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ReadRoute(AttributeData attribute) =>
        attribute.ConstructorArguments.FirstOrDefault().Value as string;

    /// <summary>메서드에 붙은 <c>[Route]</c> 템플릿들. 없으면 빈 목록이다.</summary>
    private static IReadOnlyList<string> ReadMethodRoutes(IMethodSymbol method) => method.GetAttributes()
        .Where(item => TrimAttributeSuffix(item.AttributeClass?.Name) == "Route")
        .Select(ReadRoute)
        .Where(route => route is not null)
        .Select(route => route!)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// 진입 특성의 템플릿과 메서드 <c>[Route]</c>를 합쳐 만들 라우트 목록을 만든다.
    /// 둘 다 있으면 서로를 대체하지 않고 각각 별도 라우트가 된다(ASP.NET Core 라우팅 규칙).
    /// 둘 다 없으면 클래스 라우트만 쓰는 하나(null)를 돌려준다.
    /// </summary>
    private static IReadOnlyList<string?> CombineMethodRoutes(
        string? attributeRoute, IReadOnlyList<string> methodRoutes)
    {
        var routes = new List<string?>();
        if (attributeRoute is not null)
        {
            routes.Add(attributeRoute);
        }

        foreach (var route in methodRoutes)
        {
            if (!routes.Contains(route, StringComparer.Ordinal))
            {
                routes.Add(route);
            }
        }

        return routes.Count > 0 ? routes : [null];
    }

    /// <summary>
    /// <c>[ApiExplorerSettings(IgnoreApi = true)]</c>로 문서에서 숨겼는지.
    /// 숨겨도 라우트는 실제로 존재하므로 진입점을 지우지 않고 구분만 한다.
    /// </summary>
    private static bool IsHiddenFromApiExplorer(ISymbol symbol) => symbol.GetAttributes()
        .Where(item => TrimAttributeSuffix(item.AttributeClass?.Name) == "ApiExplorerSettings")
        .SelectMany(item => item.NamedArguments)
        .Any(argument => string.Equals(argument.Key, "IgnoreApi", StringComparison.Ordinal) &&
                         argument.Value.Value is true);

    /// <summary>
    /// <c>[Authorize]</c>·<c>[AllowAnonymous]</c>를 읽는다. 정책·역할이 상수가 아니면 추정으로 낮춘다.
    /// 아무 특성도 없으면 null이다 — "인가가 없다"가 아니라 "확인하지 못했다"는 뜻이다.
    /// </summary>
    private static EndpointAuthorization? ReadAuthorization(ISymbol symbol, string file, int line)
    {
        var attributes = symbol.GetAttributes().ToArray();
        var allowAnonymous = attributes.Any(item => TrimAttributeSuffix(item.AttributeClass?.Name) == "AllowAnonymous");
        var authorize = attributes
            .Where(item => TrimAttributeSuffix(item.AttributeClass?.Name) == "Authorize")
            .ToArray();
        if (!allowAnonymous && authorize.Length == 0)
        {
            return null;
        }

        var policies = new List<string>();
        var roles = new List<string>();
        var schemes = new List<string>();
        var certain = true;
        foreach (var attribute in authorize)
        {
            if (attribute.ConstructorArguments.FirstOrDefault().Value is string positional)
            {
                policies.Add(positional);
            }
            else if (attribute.ConstructorArguments.Length > 0)
            {
                certain = false;
            }

            foreach (var (key, value) in attribute.NamedArguments)
            {
                var text = value.Value as string;
                if (text is null)
                {
                    certain = false;
                    continue;
                }

                if (key == "Policy") policies.Add(text);
                else if (key == "Roles") roles.AddRange(text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                else if (key == "AuthenticationSchemes") schemes.AddRange(text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return new EndpointAuthorization
        {
            Policies = policies.Distinct(StringComparer.Ordinal).ToArray(),
            Roles = roles.Distinct(StringComparer.Ordinal).ToArray(),
            Schemes = schemes.Distinct(StringComparer.Ordinal).ToArray(),
            AllowAnonymous = allowAnonymous,
            Confidence = certain ? CallConfidence.Certain : CallConfidence.Estimated,
            File = file,
            Line = line
        };
    }

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
    private static string CombineRoute(
        string? classRoute, string? methodRoute, INamedTypeSymbol declaringType, IMethodSymbol method)
    {
        var combined = EndpointIdentity.Join(classRoute, methodRoute).TrimStart('/');
        combined = combined
            .Replace("[controller]", RemoveControllerSuffix(declaringType.Name), StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", method.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("[area]", ReadAreaName(declaringType), StringComparison.OrdinalIgnoreCase);
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
public sealed record EntryPointScanResult(IReadOnlyList<ScannedEntryPoint> EntryPoints, IReadOnlyList<string> Notes)
{
    /// <summary>HTTP 밖 실행 단위 후보. 진입점 목록에는 넣지 않는다.</summary>
    public IReadOnlyList<EntryPointCandidateGroup> Candidates { get; init; } = [];
}

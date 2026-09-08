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

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                var model = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || model is null)
                {
                    continue;
                }

                foreach (var methodSyntax in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(methodSyntax, cancellationToken) is not IMethodSymbol method)
                    {
                        continue;
                    }

                    var callable = AnalysisCallable.ForMethod(method, methodSyntax, projectKey);
                    var line = methodSyntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    foreach (var attribute in method.GetAttributes())
                    {
                        var attributeName = TrimAttributeSuffix(attribute.AttributeClass?.Name);
                        if (!config.EntryPointAttributes.Contains(attributeName, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // 같은 메서드에 [HttpGet("a")] [HttpGet("b")]가 붙으면 라우트마다 엔드포인트가 하나씩 나온다.
                        var methodRoute = ReadRoute(attribute);
                        var classRoute = method.ContainingType.GetAttributes()
                            .Where(item => TrimAttributeSuffix(item.AttributeClass?.Name) == "Route")
                            .Select(ReadRoute)
                            .FirstOrDefault(route => route is not null);
                        var route = CombineRoute(classRoute, methodRoute, method);
                        var normalized = EndpointIdentity.NormalizeRoute(route);
                        var httpMethod = ToHttpMethod(attributeName);
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

    private static string CombineRoute(string? classRoute, string? methodRoute, IMethodSymbol method)
    {
        var left = classRoute?.Trim('/');
        var right = methodRoute?.Trim('/');
        var combined = string.Join('/', new[] { left, right }.Where(part => !string.IsNullOrWhiteSpace(part)));
        combined = combined
            .Replace("[controller]", RemoveControllerSuffix(method.ContainingType.Name), StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", method.Name, StringComparison.OrdinalIgnoreCase);
        return "/" + combined;
    }

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

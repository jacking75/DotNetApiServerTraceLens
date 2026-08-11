using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

public sealed class EntryPointScanner
{
    public async Task<IReadOnlyList<ScannedEntryPoint>> ScanAsync(
        LoadedSolution loaded,
        TraceLensConfig config,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ScannedEntryPoint>();
        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            foreach (var document in project.Documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular))
            {
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

                    foreach (var attribute in method.GetAttributes())
                    {
                        var attributeName = TrimAttributeSuffix(attribute.AttributeClass?.Name);
                        if (!config.EntryPointAttributes.Contains(attributeName, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var methodRoute = ReadRoute(attribute);
                        var classRoute = method.ContainingType.GetAttributes()
                            .Where(item => TrimAttributeSuffix(item.AttributeClass?.Name) == "Route")
                            .Select(ReadRoute)
                            .FirstOrDefault(route => route is not null);
                        var route = CombineRoute(classRoute, methodRoute, method);
                        result.Add(new ScannedEntryPoint(
                            method,
                            new EntryPoint(SymbolNames.GetNodeId(method), ToHttpMethod(attributeName), route)));
                    }
                }
            }
        }

        return result.DistinctBy(entryPoint => entryPoint.EntryPoint.NodeId).ToArray();
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

public sealed record ScannedEntryPoint(IMethodSymbol Method, EntryPoint EntryPoint);

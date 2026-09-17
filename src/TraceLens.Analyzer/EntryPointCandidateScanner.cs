using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>
/// HTTP 밖 실행 단위를 <b>세기만</b> 한다. 진입점도 엣지도 만들지 않는다.
///
/// 게임 서버·백엔드는 워커·컨슈머·gRPC가 본체인 경우가 많다. 지금 분석은 HTTP 진입점만 출발점으로 잡으므로
/// 나머지는 고립 노드로 보인다. "그런 실행 단위가 N개 있는데 이번 분석은 HTTP만 본다"를 숫자로 말해
/// 변경 영향의 "도달하는 진입점 없음"이 무엇을 세지 않은 결과인지 알 수 있게 한다.
///
/// 판정은 기반 타입·구현 인터페이스·특성으로만 한다. 이름 휴리스틱(<c>*Worker</c> 등)은 쓰지 않는다 —
/// 추정을 사실처럼 세지 않기 위해서다.
/// </summary>
public sealed class EntryPointCandidateScanner
{
    /// <param name="Kind">후보 종류. 화면·문맥에 그대로 쓴다.</param>
    /// <param name="BaseOrInterfaceNames">기반 타입 또는 구현 인터페이스 이름(제네릭 인자를 뺀 이름).</param>
    /// <param name="AttributeNames">타입 또는 멤버에 붙은 특성 이름(Attribute 접미사를 뗀 것).</param>
    private sealed record Rule(
        string Kind,
        string[] BaseOrInterfaceNames,
        string[] AttributeNames);

    private static readonly Rule[] Rules =
    [
        new("HostedService", ["BackgroundService", "IHostedService"], []),
        new("MessageConsumer", ["IConsumer", "IHandleMessages", "IMessageHandler"], []),
        new("SignalRHub", ["Hub"], []),
        new("ScheduledJob", ["IJob"], ["AutomaticRetry", "RecurringJob", "DisallowConcurrentExecution"]),
        new("AzureFunction", [], ["Function", "FunctionName"]),
    ];

    /// <summary>그룹마다 남기는 이름 수. 전체 목록을 담는 것이 목적이 아니다.</summary>
    private const int MaxSamples = 3;

    public async Task<IReadOnlyList<EntryPointCandidateGroup>> ScanAsync(
        LoadedSolution loaded,
        string solutionDirectory,
        CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        void Record(string kind, string name)
        {
            counts.TryGetValue(kind, out var count);
            counts[kind] = count + 1;
            if (!found.TryGetValue(kind, out var samples))
            {
                found[kind] = samples = new SortedSet<string>(StringComparer.Ordinal);
            }

            if (samples.Count < MaxSamples)
            {
                samples.Add(name);
            }
        }

        var conventionalRoutingHosts = 0;
        var conventionalRoutingSamples = new SortedSet<string>(StringComparer.Ordinal);
        var healthChecks = 0;

        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
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
                    if (model.GetDeclaredSymbol(typeSyntax, cancellationToken) is not INamedTypeSymbol type ||
                        type.IsAbstract)
                    {
                        continue;
                    }

                    foreach (var rule in Rules)
                    {
                        if (Matches(type, rule))
                        {
                            Record(rule.Kind, type.Name);
                        }
                    }

                    // gRPC 생성 서비스는 `Greeter.GreeterBase`처럼 중첩 기반 타입을 상속한다.
                    // 이름만 보면 평범한 `*Base`까지 걸리므로 생성기가 붙인 BindServiceMethod 특성으로 확정한다.
                    if (IsGrpcServiceBase(type.BaseType))
                    {
                        Record("GrpcService", type.Name);
                    }
                }

                // 호스트 구성에서만 나오는 등록이다. 타입이 아니라 호출식으로 센다.
                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var name = InvokedName(invocation);
                    if (name is "MapControllerRoute" or "MapDefaultControllerRoute")
                    {
                        conventionalRoutingHosts++;
                        conventionalRoutingSamples.Add(Path.GetFileName(relativePath));
                    }
                    else if (name == "MapHealthChecks")
                    {
                        healthChecks++;
                    }
                }
            }
        }

        var groups = counts
            .Select(item => new EntryPointCandidateGroup(item.Key, item.Value, [.. found[item.Key]]))
            .ToList();

        if (conventionalRoutingHosts > 0)
        {
            groups.Add(new EntryPointCandidateGroup(
                "ConventionalRouting", conventionalRoutingHosts, [.. conventionalRoutingSamples.Take(MaxSamples)]));
        }

        if (healthChecks > 0)
        {
            groups.Add(new EntryPointCandidateGroup("HealthCheck", healthChecks, []));
        }

        return groups.OrderBy(item => item.Kind, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// gRPC 생성 코드의 기반 타입인지. 중첩 타입이면서 바깥 타입에 <c>BindServiceMethod</c> 특성이 있어야 한다.
    /// 이름 규칙(<c>*Base</c>)만으로는 평범한 기반 클래스와 구분되지 않는다.
    /// </summary>
    private static bool IsGrpcServiceBase(INamedTypeSymbol? baseType)
    {
        if (baseType?.ContainingType is not { } outer)
        {
            return false;
        }

        return outer.GetAttributes()
            .Any(attribute => TrimAttributeSuffix(attribute.AttributeClass?.Name) == "BindServiceMethod");
    }

    private static bool Matches(INamedTypeSymbol type, Rule rule)
    {
        foreach (var attribute in type.GetAttributes()
            .Concat(type.GetMembers().SelectMany(member => member.GetAttributes())))
        {
            var name = TrimAttributeSuffix(attribute.AttributeClass?.Name);
            if (name is not null && rule.AttributeNames.Contains(name, StringComparer.Ordinal))
            {
                return true;
            }
        }

        if (rule.BaseOrInterfaceNames.Length == 0)
        {
            return false;
        }

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (rule.BaseOrInterfaceNames.Contains(baseType.Name, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return type.AllInterfaces.Any(item => rule.BaseOrInterfaceNames.Contains(item.Name, StringComparer.Ordinal));
    }

    private static string? InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        _ => null
    };

    private static string? TrimAttributeSuffix(string? name) =>
        name is null ? null : name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
}

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TraceLens.Analyzer.Models;

namespace TraceLens.Analyzer;

/// <summary>호출 한 자리의 성격. 규칙과 보상 판정이 모두 이 값으로 갈린다.</summary>
public enum SiteKind
{
    /// <summary>솔루션 안의 다른 메서드를 부르는 평범한 호출. 접근 요약을 전파할 때 쓴다.</summary>
    Plain,
    Db,
    Cache,
    ConnectionOpen,
    TransactionBegin,
    TransactionEnd,

    /// <summary>상태를 바꿀 수 있는 외부 호출(HTTP·푸시 등).</summary>
    External,

    /// <summary><c>.Result</c>/<c>.Wait()</c> 같은 동기 대기.</summary>
    SyncWait
}

/// <summary>
/// 메서드 본문에서 찾은 호출 한 자리. 규칙 엔진과 보상 분석이 구문 트리를 다시 열지 않아도 되도록
/// 판정에 필요한 것을 여기에 모두 담는다.
/// </summary>
/// <param name="Position">소스 순서 정렬용 위치(SpanStart).</param>
/// <param name="CalleeSymbol">패턴 매칭에 쓰는 전체 이름(<c>Ns.Type.Method</c>).</param>
/// <param name="CalleeNodeId">솔루션 안 메서드면 그 노드 id. 외부 라이브러리면 null.</param>
/// <param name="ShardKeyIdentifier">커넥션을 열 때 넘긴 첫 인자 식별자. 샤드 규칙이 쓴다.</param>
/// <param name="AssignedVariable">커넥션을 받는 지역 변수 이름. 어느 커넥션으로 실행한 쿼리인지 잇는 데 쓴다.</param>
public sealed record CallSite(
    int Position,
    int Line,
    string Name,
    string CalleeSymbol,
    string? CalleeNodeId,
    string ReceiverText,
    IReadOnlyList<string> ArgumentIdentifiers,
    SiteKind Kind,
    string DataSource,
    string Target,
    AccessOperation Operation,
    string Statement,
    bool InLoop,
    bool InTransaction,
    bool InCatch,
    bool InTryBlock,
    bool HasExpiryArgument,
    bool DynamicSql,
    bool InUsing,
    string? ShardKeyIdentifier,
    bool ShardKeyIsLiteral,
    string? AssignedVariable)
{
    public bool IsWrite => Operation is AccessOperation.Write or AccessOperation.Delete;
}

/// <summary>실패 처리 경로 하나(<c>catch</c> 블록 또는 조기 <c>return</c>이 있는 분기).</summary>
/// <param name="Kind">catch · earlyReturn.</param>
/// <param name="Guarded">보상 호출이 다시 try로 감싸여 있는지. COMP-003이 쓴다.</param>
public sealed record FailurePath(
    string Kind,
    int Position,
    int Line,
    IReadOnlyList<CallSite> Sites,
    bool Guarded);

/// <summary>메서드 하나에서 규칙 판정에 필요한 사실을 모은 것.</summary>
public sealed record MethodDataFacts(
    string NodeId,
    string DisplayName,
    string ClassId,
    string File,
    int StartLine,
    int EndLine,
    bool IsControllerAction,
    bool ReferencesUserContext,
    IReadOnlyList<string> RequestBoundParameters,
    IReadOnlyList<CallSite> Sites,
    IReadOnlyList<FailurePath> FailurePaths,
    bool HasTransaction,
    bool HasCommit);

/// <param name="MethodDataSources">메서드 노드 id → 그 메서드의 쿼리가 실행되는 데이터소스 이름.</param>
public sealed record DataAccessScanResult(
    IReadOnlyList<MethodDataFacts> Methods,
    IReadOnlyList<DataAccess> Accesses,
    IReadOnlyList<TransactionScopeInfo> Transactions,
    IReadOnlyDictionary<string, string> DataSourceKinds,
    IReadOnlyDictionary<string, string> MethodDataSources);

/// <summary>
/// 솔루션을 한 번 훑어 DB·캐시 접근과, 보상 처리·규칙 판정에 필요한 구조 정보를 모은다.
/// SQL 리터럴이 있으면 거기서 테이블과 동작을 읽고, 없으면 설정한 API 패턴과 메서드 이름으로 판정한다.
/// </summary>
public sealed class DataAccessScanner(DiResolver diResolver, ILogger<DataAccessScanner> logger)
{
    private static readonly Regex SqlVerb = new(
        @"^\s*(?<verb>SELECT|INSERT|UPDATE|DELETE|MERGE|REPLACE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// SQL 문장인지 한 번 더 확인하는 조건. 동사만 보면 <c>StartsWith("Delete")</c> 같은 평범한 문자열까지 쿼리로 오해한다.
    /// </summary>
    private static readonly Regex SqlClause = new(
        @"\b(FROM|INTO|SET|VALUES|WHERE|JOIN|TABLE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SqlTable = new(
        @"\b(?:FROM|INTO|UPDATE|JOIN)\s+(?<table>[A-Za-z_][A-Za-z0-9_\.\[\]]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<DataAccessScanResult> ScanAsync(
        LoadedSolution loaded,
        TraceLensConfig config,
        string solutionDirectory,
        CancellationToken cancellationToken = default)
    {
        var methods = new List<MethodDataFacts>();
        var accesses = new List<DataAccess>();
        var transactions = new List<TransactionScopeInfo>();
        var dataSourceKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                var model = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || model is null)
                {
                    continue;
                }

                foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
                {
                    if (member is not (MethodDeclarationSyntax or ConstructorDeclarationSyntax))
                    {
                        continue;
                    }

                    if (model.GetDeclaredSymbol(member, cancellationToken) is not IMethodSymbol symbol)
                    {
                        continue;
                    }

                    var facts = BuildFacts(member, symbol, model, config, relativePath, solutionDirectory, cancellationToken);
                    if (facts is null)
                    {
                        continue;
                    }

                    methods.Add(facts);
                    CollectAccesses(facts, accesses, dataSourceKinds, config);
                    CollectTransaction(facts, transactions);
                }
            }
        }

        var methodDataSources = ResolveMethodDataSources(methods);
        for (var index = 0; index < accesses.Count; index++)
        {
            if (accesses[index].Storage != StorageKind.Database || accesses[index].DataSource != FallbackDataSource)
            {
                continue;
            }

            if (methodDataSources.TryGetValue(accesses[index].MethodNodeId, out var resolvedSource))
            {
                accesses[index] = accesses[index] with { DataSource = resolvedSource };
                dataSourceKinds.TryAdd(resolvedSource, "single");
            }
        }

        logger.LogInformation(
            "DB·캐시 접근 {AccessCount}곳 · 트랜잭션 {TransactionCount}개를 찾았다.",
            accesses.Count,
            transactions.Count);
        return new DataAccessScanResult(methods, accesses, transactions, dataSourceKinds, methodDataSources);
    }

    /// <summary>기본 데이터소스 이름. 커넥션을 어디서 열었는지 못 찾았을 때 쓴다.</summary>
    internal const string FallbackDataSource = "Db";

    /// <summary>
    /// 리포지터리는 커넥션을 인자로 받기만 한다. 그래서 호출자가 연 커넥션 변수를 인자에서 찾아
    /// "이 메서드의 쿼리는 어느 데이터소스에서 도는지"를 채운다(한 홉).
    /// </summary>
    private static IReadOnlyDictionary<string, string> ResolveMethodDataSources(IReadOnlyList<MethodDataFacts> methods)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var facts in methods)
        {
            var own = facts.Sites.FirstOrDefault(site => site.Kind == SiteKind.ConnectionOpen);
            if (own is not null)
            {
                resolved[facts.NodeId] = own.DataSource;
            }
        }

        foreach (var facts in methods)
        {
            var variables = facts.Sites
                .Where(site => site.Kind == SiteKind.ConnectionOpen && site.AssignedVariable is not null)
                .GroupBy(site => site.AssignedVariable!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().DataSource, StringComparer.Ordinal);
            if (variables.Count == 0)
            {
                continue;
            }

            foreach (var site in facts.Sites)
            {
                if (site.CalleeNodeId is null || resolved.ContainsKey(site.CalleeNodeId))
                {
                    continue;
                }

                var match = site.ArgumentIdentifiers.FirstOrDefault(variables.ContainsKey);
                if (match is not null)
                {
                    resolved[site.CalleeNodeId] = variables[match];
                }
            }
        }

        return resolved;
    }

    private MethodDataFacts? BuildFacts(
        MemberDeclarationSyntax member,
        IMethodSymbol symbol,
        SemanticModel model,
        TraceLensConfig config,
        string relativePath,
        string solutionDirectory,
        CancellationToken cancellationToken)
    {
        SyntaxNode? body = member switch
        {
            MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
            ConstructorDeclarationSyntax constructor => constructor.Body,
            _ => null
        };
        if (body is null)
        {
            return null;
        }

        var sites = new List<CallSite>();
        var transactionDepth = 0;
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()
                     .OrderBy(node => node.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var site = BuildSite(invocation, model, config, solutionDirectory, transactionDepth > 0, cancellationToken);
            if (site is null)
            {
                continue;
            }

            if (site.Kind == SiteKind.TransactionBegin)
            {
                transactionDepth++;
            }
            else if (site.Kind == SiteKind.TransactionEnd && transactionDepth > 0)
            {
                transactionDepth--;
            }

            sites.Add(site);
        }

        foreach (var wait in EnumerateSyncWaits(body))
        {
            sites.Add(wait);
        }

        if (sites.Count == 0)
        {
            return null;
        }

        sites.Sort((left, right) => left.Position.CompareTo(right.Position));
        var lineSpan = member.GetLocation().GetLineSpan();
        var isAction = IsControllerAction(symbol);
        return new MethodDataFacts(
            SymbolNames.GetNodeId(symbol),
            SymbolNames.GetDisplayName(symbol),
            SymbolNames.GetClassId(symbol.ContainingType),
            relativePath,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.EndLinePosition.Line + 1,
            isAction,
            isAction && ReferencesUserContext(body),
            isAction ? GetRequestBoundParameters(symbol) : [],
            sites,
            CollectFailurePaths(body, sites),
            sites.Any(site => site.Kind == SiteKind.TransactionBegin),
            sites.Any(site => site.Kind == SiteKind.TransactionEnd));
    }

    private CallSite? BuildSite(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        TraceLensConfig config,
        string solutionDirectory,
        bool inTransaction,
        CancellationToken cancellationToken)
    {
        var symbolInfo = model.GetSymbolInfo(invocation, cancellationToken);
        if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol called)
        {
            return null;
        }

        var method = called.ReducedFrom ?? called;
        var containingType = method.ContainingType;
        var calleeSymbol = $"{containingType?.ToDisplayString() ?? string.Empty}.{method.Name}";
        var name = method.Name;
        var options = config.Db;
        var receiverText = invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression.ToString()
            : string.Empty;
        var arguments = invocation.ArgumentList.Arguments;
        var argumentIdentifiers = arguments
            .Select(argument => GetIdentifier(argument.Expression))
            .Where(identifier => identifier is not null)
            .Select(identifier => identifier!)
            .ToArray();

        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var inLoop = HasAncestor(invocation, node => node is ForStatementSyntax or ForEachStatementSyntax
            or WhileStatementSyntax or DoStatementSyntax);
        var inCatch = HasAncestor(invocation, node => node is CatchClauseSyntax);
        var inTry = !inCatch && HasAncestor(invocation, node => node is TryStatementSyntax);

        var (statement, dynamicSql) = ReadStringArgument(arguments, model, cancellationToken);
        var isSql = statement is not null && SqlVerb.IsMatch(statement) && SqlClause.IsMatch(statement);
        var matchesQueryApi = PatternMatch.MatchesAny(calleeSymbol, options.QueryApis);
        var isCacheApi = IsCacheApi(calleeSymbol, containingType, options);
        var isConnectionOpen = IsConnectionOpen(calleeSymbol, containingType, method, options);
        var isSolutionMethod = InvocationResolver.IsSolutionMethod(method, solutionDirectory, config);

        var kind = SiteKind.Plain;
        var operation = AccessOperation.Other;
        var target = string.Empty;
        var dataSource = string.Empty;
        var display = statement ?? string.Empty;
        string? shardKey = null;
        string? assignedVariable = null;
        var shardKeyIsLiteral = false;
        var hasExpiry = false;

        if (name.StartsWith("BeginTransaction", StringComparison.Ordinal))
        {
            kind = SiteKind.TransactionBegin;
        }
        else if (name is "Commit" or "CommitAsync" or "Rollback" or "RollbackAsync")
        {
            kind = SiteKind.TransactionEnd;
        }
        else if (isConnectionOpen)
        {
            kind = SiteKind.ConnectionOpen;
            dataSource = ResolveDataSourceName(containingType, method, options);
            var first = arguments.Count > 0 ? arguments[0].Expression : null;
            shardKey = first is null ? null : GetIdentifier(first) ?? first.ToString();
            shardKeyIsLiteral = first is LiteralExpressionSyntax;
            assignedVariable = invocation.FirstAncestorOrSelf<VariableDeclaratorSyntax>()?.Identifier.Text;
        }
        else if (isCacheApi)
        {
            kind = SiteKind.Cache;
            operation = ClassifyCacheOperation(name);
            dataSource = ResolveCacheName(containingType, options);
            target = ReadKeyArgument(arguments, model, cancellationToken);
            display = $"{ClassifyCacheCommand(name)} {target}".Trim();
            hasExpiry = HasExpiryArgument(arguments, model, cancellationToken);
        }
        else if (isSql || matchesQueryApi)
        {
            kind = SiteKind.Db;
            operation = isSql ? ClassifySqlOperation(statement!) : ClassifyByName(name);
            target = isSql ? ReadTableName(statement!) : statement ?? string.Empty;
            dataSource = string.Empty; // 커넥션 변수로 나중에 채운다.
            display = statement ?? name;
        }
        else if (PatternMatch.MatchesAny(calleeSymbol, options.ExternalApis) ||
                 PatternMatch.MatchesAny(calleeSymbol, options.Compensation.Irreversible))
        {
            kind = SiteKind.External;
            operation = AccessOperation.Write;
            display = $"{containingType?.Name}.{name}";
            target = display;
        }
        else if (!isSolutionMethod)
        {
            return null;
        }

        return new CallSite(
            invocation.SpanStart,
            line,
            name,
            calleeSymbol,
            isSolutionMethod ? SymbolNames.GetNodeId(ResolveCallee(method)) : null,
            receiverText,
            argumentIdentifiers,
            kind,
            dataSource,
            target,
            operation,
            display,
            inLoop,
            inTransaction,
            inCatch,
            inTry,
            hasExpiry,
            dynamicSql && isSql,
            IsInUsing(invocation),
            shardKey,
            shardKeyIsLiteral,
            assignedVariable);
    }

    /// <summary>인터페이스 호출은 DI 등록으로 구현체까지 연결해야 호출 그래프의 노드 id와 맞는다.</summary>
    private IMethodSymbol ResolveCallee(IMethodSymbol method) =>
        method.ContainingType?.TypeKind == TypeKind.Interface ? diResolver.Resolve(method) ?? method : method;

    /// <summary>메서드 안의 <c>.Result</c>/<c>.Wait()</c>를 동기 대기 자리로 모은다.</summary>
    private static IEnumerable<CallSite> EnumerateSyncWaits(SyntaxNode body)
    {
        foreach (var node in body.DescendantNodes())
        {
            var text = node switch
            {
                MemberAccessExpressionSyntax member when member.Name.Identifier.Text == "Result" => ".Result",
                InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax member2 &&
                    member2.Name.Identifier.Text is "Wait" or "GetAwaiter" => $".{member2.Name.Identifier.Text}()",
                _ => null
            };
            if (text is null)
            {
                continue;
            }

            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            yield return new CallSite(
                node.SpanStart, line, text, text, null, node.ToString(), [], SiteKind.SyncWait,
                string.Empty, string.Empty, AccessOperation.Other, node.ToString().Trim(),
                false, false, false, false, false, false, false, null, false, null);
        }
    }

    private static IReadOnlyList<FailurePath> CollectFailurePaths(SyntaxNode body, IReadOnlyList<CallSite> sites)
    {
        var paths = new List<FailurePath>();
        foreach (var clause in body.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            var span = clause.Span;
            paths.Add(new FailurePath(
                "catch",
                clause.SpanStart,
                clause.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                sites.Where(site => span.Contains(site.Position)).ToArray(),
                clause.DescendantNodes().OfType<TryStatementSyntax>().Any()));
        }

        // 예외가 아니라 값으로 빠져나가는 경로. 마지막 문장으로서의 return은 정상 종료라 제외한다.
        foreach (var statement in body.DescendantNodes().OfType<ReturnStatementSyntax>())
        {
            if (HasAncestor(statement, node => node is CatchClauseSyntax or LambdaExpressionSyntax))
            {
                continue;
            }

            var guard = statement.FirstAncestorOrSelf<IfStatementSyntax>();
            if (guard is null)
            {
                continue;
            }

            var span = guard.Span;
            paths.Add(new FailurePath(
                "earlyReturn",
                statement.SpanStart,
                statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                sites.Where(site => span.Contains(site.Position)).ToArray(),
                false));
        }

        return paths.OrderBy(path => path.Position).ToArray();
    }

    /// <summary>메서드가 실제로 연 커넥션을 짚어 DB 접근에 데이터소스 이름을 붙인다.</summary>
    private static void CollectAccesses(
        MethodDataFacts facts,
        List<DataAccess> accesses,
        Dictionary<string, string> dataSourceKinds,
        TraceLensConfig config)
    {
        var currentDataSource = facts.Sites.FirstOrDefault(site => site.Kind == SiteKind.ConnectionOpen)?.DataSource;
        foreach (var site in facts.Sites)
        {
            if (site.Kind == SiteKind.ConnectionOpen)
            {
                currentDataSource = site.DataSource;
                dataSourceKinds.TryAdd(site.DataSource, "single");
                continue;
            }

            if (site.Kind is not (SiteKind.Db or SiteKind.Cache))
            {
                continue;
            }

            var dataSource = site.Kind == SiteKind.Cache
                ? site.DataSource
                : site.DataSource.Length > 0 ? site.DataSource : currentDataSource ?? "Db";
            dataSourceKinds.TryAdd(dataSource, site.Kind == SiteKind.Cache ? "cache" : "single");
            accesses.Add(new DataAccess(
                $"{facts.NodeId}#{site.Line}#{accesses.Count}",
                site.Kind == SiteKind.Cache ? StorageKind.Cache : StorageKind.Database,
                dataSource,
                site.Target.Length > 0 ? site.Target : site.Name,
                site.Operation,
                site.Statement,
                facts.NodeId,
                facts.DisplayName,
                facts.ClassId,
                facts.File,
                site.Line,
                site.InTransaction,
                site.InLoop));
        }
    }

    private static void CollectTransaction(MethodDataFacts facts, List<TransactionScopeInfo> transactions)
    {
        if (!facts.HasTransaction)
        {
            return;
        }

        var begin = facts.Sites.First(site => site.Kind == SiteKind.TransactionBegin);
        var inside = facts.Sites.Where(site => site.Position > begin.Position).ToArray();
        var dataSources = facts.Sites
            .Where(site => site.Kind == SiteKind.ConnectionOpen)
            .Select(site => site.DataSource)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        transactions.Add(new TransactionScopeInfo(
            $"tx:{facts.NodeId}",
            facts.NodeId,
            facts.DisplayName,
            facts.ClassId,
            facts.File,
            begin.Line,
            facts.HasCommit,
            dataSources,
            [],
            inside.Where(site => site.Kind == SiteKind.External && site.InTransaction)
                .Select(site => $"{site.Statement} ({site.Line}줄)").ToArray(),
            inside.Where(site => site.Kind == SiteKind.Cache && site.InTransaction)
                .Select(site => $"{site.Statement} ({site.Line}줄)").ToArray()));
    }

    /* ------------------------------------------------------------------ 판정 도우미 */

    private static bool IsCacheApi(string calleeSymbol, INamedTypeSymbol? containingType, DataAccessOptions options)
    {
        if (containingType is null)
        {
            return false;
        }

        if (PatternMatch.MatchesAny(calleeSymbol, options.CacheApis))
        {
            return true;
        }

        var typeName = containingType.Name;
        return typeName.Contains("Cache", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("Redis", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConnectionOpen(
        string calleeSymbol, INamedTypeSymbol? containingType, IMethodSymbol method, DataAccessOptions options)
    {
        if (PatternMatch.MatchesAny(calleeSymbol, options.ConnectionFactories))
        {
            return true;
        }

        if (containingType is null)
        {
            return false;
        }

        var isFactoryType = containingType.Name.Contains("ConnectionFactory", StringComparison.OrdinalIgnoreCase) ||
                            containingType.Name.Contains("ConnectionPool", StringComparison.OrdinalIgnoreCase);
        return isFactoryType && (method.Name.StartsWith("Open", StringComparison.Ordinal) ||
                                 method.Name.StartsWith("Create", StringComparison.Ordinal) ||
                                 method.Name.StartsWith("Get", StringComparison.Ordinal));
    }

    private static string ResolveDataSourceName(
        INamedTypeSymbol? containingType, IMethodSymbol method, DataAccessOptions options)
    {
        var typeName = containingType?.ToDisplayString() ?? method.Name;
        var configured = options.DataSources.FirstOrDefault(source =>
            source.TypePatterns.Any(pattern => PatternMatch.Matches(typeName, pattern)));
        if (configured is not null)
        {
            return configured.Name;
        }

        var shortName = containingType?.Name ?? "Db";
        if (shortName.StartsWith("I", StringComparison.Ordinal) && shortName.Length > 1 && char.IsUpper(shortName[1]))
        {
            shortName = shortName[1..];
        }

        foreach (var suffix in new[] { "ConnectionFactory", "ConnectionPool", "Factory", "Connection" })
        {
            if (shortName.EndsWith(suffix, StringComparison.Ordinal))
            {
                shortName = shortName[..^suffix.Length];
                break;
            }
        }

        return shortName.Length == 0 ? "Db" : shortName;
    }

    private static string ResolveCacheName(INamedTypeSymbol? containingType, DataAccessOptions options)
    {
        var typeName = containingType?.ToDisplayString() ?? "Cache";
        var configured = options.DataSources.FirstOrDefault(source =>
            source.TypePatterns.Any(pattern => PatternMatch.Matches(typeName, pattern)));
        if (configured is not null)
        {
            return configured.Name;
        }

        return typeName.Contains("Redis", StringComparison.OrdinalIgnoreCase) ? "Redis" : "Cache";
    }

    private static AccessOperation ClassifySqlOperation(string sql)
    {
        var verb = SqlVerb.Match(sql).Groups["verb"].Value.ToUpperInvariant();
        return verb switch
        {
            "SELECT" => AccessOperation.Read,
            "DELETE" => AccessOperation.Delete,
            "INSERT" or "UPDATE" or "MERGE" or "REPLACE" => AccessOperation.Write,
            _ => AccessOperation.Other
        };
    }

    private static string ReadTableName(string sql)
    {
        var match = SqlTable.Match(sql);
        return match.Success ? match.Groups["table"].Value.Trim('[', ']') : string.Empty;
    }

    private static AccessOperation ClassifyByName(string name)
    {
        var trimmed = name.EndsWith("Async", StringComparison.Ordinal) ? name[..^5] : name;
        if (trimmed.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Remove", StringComparison.OrdinalIgnoreCase))
        {
            return AccessOperation.Delete;
        }

        if (trimmed.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Load", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Find", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Query", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Select", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Read", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("List", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Count", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Exists", StringComparison.OrdinalIgnoreCase))
        {
            return AccessOperation.Read;
        }

        return AccessOperation.Write;
    }

    private static AccessOperation ClassifyCacheOperation(string name)
    {
        var trimmed = name.EndsWith("Async", StringComparison.Ordinal) ? name[..^5] : name;
        if (trimmed.StartsWith("Remove", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("KeyDelete", StringComparison.OrdinalIgnoreCase))
        {
            return AccessOperation.Delete;
        }

        if (trimmed.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("StringGet", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Hash Get", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("HashGet", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Read", StringComparison.OrdinalIgnoreCase))
        {
            return AccessOperation.Read;
        }

        if (trimmed is "Keys" or "FlushAll" or "FlushDatabase" or "FlushAllDatabases")
        {
            return AccessOperation.Other;
        }

        return AccessOperation.Write;
    }

    private static string ClassifyCacheCommand(string name)
    {
        var trimmed = name.EndsWith("Async", StringComparison.Ordinal) ? name[..^5] : name;
        return trimmed switch
        {
            "Keys" => "KEYS",
            "FlushAll" or "FlushDatabase" or "FlushAllDatabases" => "FLUSH",
            _ when trimmed.StartsWith("SetIfNotExists", StringComparison.OrdinalIgnoreCase) => "SET NX",
            _ when trimmed.StartsWith("Increment", StringComparison.OrdinalIgnoreCase) => "INCR",
            _ when trimmed.StartsWith("Hash", StringComparison.OrdinalIgnoreCase) => "HSET",
            _ when trimmed.StartsWith("Remove", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) => "DEL",
            _ when trimmed.StartsWith("Get", StringComparison.OrdinalIgnoreCase) => "GET",
            _ => "SET"
        };
    }

    /// <summary>
    /// 첫 문자열 인자를 읽는다. 상수는 값 그대로, 보간 문자열은 <c>{식별자}</c>로 정규화해 캐시 키 패턴을 만든다.
    /// 반환값의 두 번째 항목은 "상수가 아닌 값이 끼어 있는지"다(SEC-001 판정에 쓴다).
    /// </summary>
    private static (string? Text, bool Dynamic) ReadStringArgument(
        IReadOnlyList<ArgumentSyntax> arguments, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var argument in arguments)
        {
            var expression = argument.Expression;
            var constant = model.GetConstantValue(expression, cancellationToken);
            if (constant.HasValue && constant.Value is string text)
            {
                return (Normalize(text), false);
            }

            if (expression is InterpolatedStringExpressionSyntax interpolated)
            {
                var builder = new StringBuilder();
                foreach (var content in interpolated.Contents)
                {
                    if (content is InterpolatedStringTextSyntax literal)
                    {
                        builder.Append(literal.TextToken.ValueText);
                        continue;
                    }

                    if (content is InterpolationSyntax interpolation)
                    {
                        builder.Append('{').Append(GetIdentifier(interpolation.Expression) ?? "…").Append('}');
                    }
                }

                return (Normalize(builder.ToString()), true);
            }

            if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
            {
                var text2 = binary.ToString();
                if (text2.Contains('"'))
                {
                    return (Normalize(text2.Replace("\"", string.Empty)), true);
                }
            }
        }

        return (null, false);

        static string Normalize(string value) =>
            string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    /// <summary>캐시 키는 항상 첫 인자다. 상수·보간 문자열이면 정규화하고, 변수면 그 이름을 그대로 쓴다.</summary>
    private static string ReadKeyArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel model, CancellationToken cancellationToken)
    {
        if (arguments.Count == 0)
        {
            return string.Empty;
        }

        var (text, _) = ReadStringArgument([arguments[0]], model, cancellationToken);
        return text ?? arguments[0].Expression.ToString();
    }

    private static bool HasExpiryArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var argument in arguments)
        {
            if (argument.NameColon?.Name.Identifier.Text.Contains("expir", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            var type = model.GetTypeInfo(argument.Expression, cancellationToken).Type;
            if (type?.Name is "TimeSpan" or "DateTimeOffset" or "DateTime" or "DistributedCacheEntryOptions")
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetIdentifier(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        _ => null
    };

    private static bool HasAncestor(SyntaxNode node, Func<SyntaxNode, bool> predicate)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is MemberDeclarationSyntax)
            {
                return false;
            }

            if (predicate(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>using</c>/<c>await using</c>으로 감싼 자리인지 확인한다(CONN-001).</summary>
    private static bool IsInUsing(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case UsingStatementSyntax:
                    return true;
                case LocalDeclarationStatementSyntax local:
                    return local.UsingKeyword != default;
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    private static bool IsControllerAction(IMethodSymbol method)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        return method.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.Name.StartsWith("Http", StringComparison.Ordinal) == true &&
            attribute.AttributeClass.Name.EndsWith("Attribute", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> GetRequestBoundParameters(IMethodSymbol method) => method.Parameters
        .Where(parameter => parameter.GetAttributes().Any(attribute =>
                                attribute.AttributeClass?.Name is "FromBodyAttribute" or "FromQueryAttribute"
                                    or "FromFormAttribute" or "FromRouteAttribute") ||
                            parameter.Type.TypeKind == TypeKind.Class && parameter.Type.SpecialType == SpecialType.None)
        .Select(parameter => parameter.Name)
        .ToArray();

    /// <summary>인증 컨텍스트(<c>User</c>·<c>HttpContext</c>)를 한 번이라도 참조하는지. SHARD-005 판정에 쓴다.</summary>
    private static bool ReferencesUserContext(SyntaxNode body) => body.DescendantNodes()
        .OfType<SimpleNameSyntax>()
        .Any(name => name.Identifier.Text is "User" or "HttpContext" or "ClaimsPrincipal" or "UserContext"
            or "CurrentUser" or "userContext");
}

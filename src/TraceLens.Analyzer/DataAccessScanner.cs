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
public sealed record DataCallSite(
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

    /// <summary>이 자리를 감싼 트랜잭션 래퍼 메서드의 표시 이름. 래퍼 람다 안이 아니면 null이다.</summary>
    public string? TransactionWrapper { get; init; }

    /// <summary>이 자리를 얼마나 확실하게 짚었는지.</summary>
    public FindingConfidence Confidence { get; init; } = FindingConfidence.Certain;

    /// <summary>추정일 때 그 사유 한 줄.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>
    /// SQL 리터럴이 아니라 빌더 체인에서 합성한 <c>Statement</c>인지.
    /// SQL 본문을 읽는 규칙(RW·PERF·IDX)은 합성 문장을 검사하지 않는다.
    /// </summary>
    public bool IsSynthesized { get; init; }
}

/// <summary>실패 처리 경로 하나(<c>catch</c> 블록 또는 조기 <c>return</c>이 있는 분기).</summary>
/// <param name="Kind">catch · earlyReturn.</param>
/// <param name="Guarded">보상 호출이 다시 try로 감싸여 있는지. COMP-003이 쓴다.</param>
public sealed record FailurePath(
    string Kind,
    int Position,
    int Line,
    IReadOnlyList<DataCallSite> Sites,
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
    IReadOnlyList<DataCallSite> Sites,
    IReadOnlyList<FailurePath> FailurePaths,
    bool HasTransaction,
    bool HasCommit)
{
    /// <summary>이 메서드가 부른 트랜잭션 래퍼 호출들. 경계는 래퍼 안에 있어 Begin/Commit이 보이지 않는다.</summary>
    public IReadOnlyList<WrapperTransaction> Wrappers { get; init; } = [];
}

/// <param name="Name">래퍼 메서드의 표시 이름.</param>
/// <param name="AccessIds">이 경계 안에서 일어난 접근 id(수집 뒤 채운다).</param>
public sealed record WrapperTransaction(int Position, int Line, string Name);

/// <param name="MethodDataSources">메서드 노드 id → 그 메서드의 쿼리가 실행되는 데이터소스 이름.</param>
public sealed record DataAccessScanResult(
    IReadOnlyList<MethodDataFacts> Methods,
    IReadOnlyList<DataAccess> Accesses,
    IReadOnlyList<TransactionScopeInfo> Transactions,
    IReadOnlyDictionary<string, string> DataSourceKinds,
    IReadOnlyDictionary<string, string> MethodDataSources)
{
    /// <summary>
    /// 솔루션에서 본 ORM 타입 이름(예: <c>DbContext</c> 파생, <c>IMongoCollection</c>, <c>ISession</c>).
    /// 접근이 0건인데 이것이 있으면 "DB를 안 쓴다"가 아니라 "우리가 못 읽었다"는 뜻이다.
    /// </summary>
    public IReadOnlyList<string> OrmTypeHints { get; init; } = [];
}

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
        var ormHints = new SortedSet<string>(StringComparer.Ordinal);
        var transactions = new List<TransactionScopeInfo>();
        var dataSourceKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenAccesses = new HashSet<(string File, int Line, string Target, AccessOperation Operation)>();
        var autoWrappers = await DetectTransactionWrappersAsync(loaded, cancellationToken);
        if (autoWrappers.Count > 0)
        {
            logger.LogInformation(
                "트랜잭션 래퍼 {Count}개를 본문에서 찾았다: {Names}",
                autoWrappers.Count,
                string.Join(", ", autoWrappers.Take(5)));
        }

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
                CollectOrmHints(root, ormHints);

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

                    var facts = BuildFacts(
                        member, symbol, model, config, relativePath, solutionDirectory, autoWrappers, cancellationToken);
                    if (facts is null)
                    {
                        continue;
                    }

                    methods.Add(facts);
                    CollectAccesses(facts, accesses, dataSourceKinds, seenAccesses, config);
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
        return new DataAccessScanResult(methods, accesses, transactions, dataSourceKinds, methodDataSources)
        {
            OrmTypeHints = [.. ormHints]
        };
    }

    /// <summary>
    /// 이 버전이 읽지 않는 ORM의 흔적을 이름만으로 모은다.
    /// 심볼 해석 없이 구문만 보므로 비용이 거의 없고, 패키지가 없는 픽스처에서도 같은 판단을 한다.
    /// 여기서 모은 것은 <b>접근을 만들지 않는다</b> — "못 읽었다"를 말하는 데만 쓴다.
    /// </summary>
    private static void CollectOrmHints(SyntaxNode root, SortedSet<string> hints)
    {
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            foreach (var baseType in type.BaseList?.Types ?? default)
            {
                var name = baseType.Type switch
                {
                    SimpleNameSyntax simple => simple.Identifier.ValueText,
                    QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
                    _ => null
                };
                if (name is "DbContext" or "IdentityDbContext")
                {
                    hints.Add("DbContext");
                }
            }
        }

        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            switch (name.Identifier.ValueText)
            {
                case "IMongoCollection":
                    hints.Add("IMongoCollection<>");
                    break;
                case "IMongoDatabase":
                    hints.Add("IMongoDatabase");
                    break;
                case "ISessionFactory":
                    hints.Add("ISessionFactory");
                    break;
            }
        }
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
        IReadOnlySet<string> autoWrappers,
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

        var wrapperCalls = CollectWrapperCalls(body, model, config, autoWrappers, cancellationToken);
        var sites = new List<DataCallSite>();
        var transactionDepth = 0;
        // 호출식과 객체 생성을 한 순서로 훑는다. `new TransactionScope(...)`는 호출식이 아니라
        // ObjectCreationExpressionSyntax라 예전에는 트랜잭션 경계로 잡히지 않았다.
        foreach (var node in body.DescendantNodes()
                     .Where(node => node is InvocationExpressionSyntax or ObjectCreationExpressionSyntax)
                     .OrderBy(node => node.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is ObjectCreationExpressionSyntax creation)
            {
                var scopeSite = BuildTransactionScopeSite(creation, model, cancellationToken);
                if (scopeSite is null)
                {
                    continue;
                }

                transactionDepth++;
                sites.Add(scopeSite);
                continue;
            }

            var invocation = (InvocationExpressionSyntax)node;
            var enclosingWrapper = FindEnclosingWrapper(invocation, wrapperCalls);
            var site = BuildSite(
                invocation, model, config, solutionDirectory,
                transactionDepth > 0 || enclosingWrapper is not null,
                enclosingWrapper?.Name,
                cancellationToken);
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
            sites.Any(site => site.Kind == SiteKind.TransactionEnd))
        {
            Wrappers = wrapperCalls.Select(item => item.Call).ToArray()
        };
    }

    /// <summary>
    /// <c>new TransactionScope(...)</c>을 트랜잭션 경계로 만든다.
    /// 호출식이 아니라 객체 생성이라 예전에는 이 경계가 통째로 빠져, 안에서 쓰는 코드가
    /// "트랜잭션 없는 쓰기"로 <b>확실</b>하게 오판정됐다(COMP-001/002 오탐).
    ///
    /// <c>using</c> 선언·블록이 경계 범위다. <c>scope.Complete()</c>는 평범한 호출이라
    /// <see cref="SiteKind.TransactionEnd"/>로 따로 잡는다.
    /// </summary>
    private static DataCallSite? BuildTransactionScopeSite(
        ObjectCreationExpressionSyntax creation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var type = model.GetTypeInfo(creation, cancellationToken).Type;
        if (type is null ||
            type.Name != "TransactionScope" ||
            type.ContainingNamespace?.ToDisplayString() != "System.Transactions")
        {
            return null;
        }

        var line = creation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var inUsing = HasAncestor(creation, node => node is UsingStatementSyntax) ||
            creation.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>()?.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) == true;
        return new DataCallSite(
            creation.SpanStart,
            line,
            "TransactionScope",
            "System.Transactions.TransactionScope..ctor",
            null,
            string.Empty,
            [],
            SiteKind.TransactionBegin,
            string.Empty,
            string.Empty,
            AccessOperation.Other,
            "new TransactionScope(...)",
            HasAncestor(creation, node => node is ForStatementSyntax or ForEachStatementSyntax
                or WhileStatementSyntax or DoStatementSyntax),
            true,
            HasAncestor(creation, node => node is CatchClauseSyntax),
            HasAncestor(creation, node => node is TryStatementSyntax),
            false,
            false,
            inUsing,
            null,
            false,
            creation.FirstAncestorOrSelf<VariableDeclaratorSyntax>()?.Identifier.Text)
        {
            Note = "using 범위가 트랜잭션 경계다."
        };
    }

    private DataCallSite? BuildSite(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        TraceLensConfig config,
        string solutionDirectory,
        bool inTransaction,
        string? transactionWrapper,
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
        var isSolutionMethod = InvocationResolver.IsSolutionMethod(method, solutionDirectory, config);
        var isCacheApi = IsCacheApi(calleeSymbol, containingType, options, isSolutionMethod);
        var isConnectionOpen = IsConnectionOpen(calleeSymbol, containingType, method, options);
        var builder = isSql || matchesQueryApi
            ? null
            : ResolveQueryBuilderSite(invocation, method, model, options, cancellationToken);

        var kind = SiteKind.Plain;
        var operation = AccessOperation.Other;
        var target = string.Empty;
        var dataSource = string.Empty;
        var display = statement ?? string.Empty;
        string? shardKey = null;
        string? assignedVariable = null;
        var shardKeyIsLiteral = false;
        var hasExpiry = false;
        var isSynthesized = false;
        var confidence = FindingConfidence.Certain;
        var note = string.Empty;
        var inTransactionByArgument = false;

        if (name.StartsWith("BeginTransaction", StringComparison.Ordinal))
        {
            kind = SiteKind.TransactionBegin;
        }
        else if (name is "Commit" or "CommitAsync" or "Rollback" or "RollbackAsync")
        {
            kind = SiteKind.TransactionEnd;
        }
        else if (name == "Complete" && containingType?.Name == "TransactionScope")
        {
            // TransactionScope의 커밋이다. 이 호출이 없으면 범위를 벗어날 때 롤백된다.
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
            var cacheTarget = ReadCacheTarget(invocation, arguments, model, cancellationToken);
            target = cacheTarget.Text;
            display = $"{ClassifyCacheCommand(name)} {target}".Trim();
            hasExpiry = HasExpiryArgument(arguments, model, cancellationToken);
            if (!cacheTarget.Certain)
            {
                confidence = FindingConfidence.Estimated;
                note = cacheTarget.Note;
            }
        }
        else if (builder is not null)
        {
            kind = SiteKind.Db;
            operation = builder.Operation;
            target = builder.Table;
            dataSource = string.Empty;
            display = builder.Statement;
            isSynthesized = true;
            inTransactionByArgument = HasTransactionArgument(arguments, model, cancellationToken);
            if (!builder.TableCertain)
            {
                confidence = FindingConfidence.Estimated;
                note = $"\ubc14\uc778\ub354 \uccb4\uc778\uc758 \ud14c\uc774\ube14 \uc778\uc790\uac00 \uc0c1\uc218\uac00 \uc544\ub2c8\ub2e4: {builder.TableExpression}";
            }
        }
        else if (isSql || matchesQueryApi)
        {
            kind = SiteKind.Db;
            dataSource = string.Empty; // 커넥션 변수로 나중에 채운다.

            // 저장 프로시저는 첫 인자가 SQL이 아니라 프로시저 이름이다.
            // 메서드 이름(QueryAsync)만 보고 Read로 단정하면 "확실"로 틀린다.
            if (IsStoredProcedureCall(invocation, arguments, model, cancellationToken))
            {
                target = statement ?? name;
                display = $"EXEC {target}".Trim();
                if (name.StartsWith("Execute", StringComparison.Ordinal))
                {
                    // Execute*는 쓰기 후보로 남기되 본문을 읽지 못했으므로 확실도를 낮춘다.
                    operation = AccessOperation.Write;
                    confidence = FindingConfidence.Estimated;
                    note = "저장 프로시저 호출이다. 본문을 읽지 않아 어떤 테이블을 바꾸는지 확정하지 못했다.";
                }
                else
                {
                    operation = AccessOperation.Unknown;
                    confidence = FindingConfidence.Estimated;
                    note = "저장 프로시저는 본문을 읽지 않아 읽기/쓰기를 확정하지 못했다.";
                }
            }
            else
            {
                operation = isSql ? ClassifySqlOperation(statement!) : ClassifyByName(name);
                target = isSql ? ReadTableName(statement!) : statement ?? string.Empty;
                display = statement ?? name;
            }
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

        inTransaction = inTransaction || inTransactionByArgument;

        return new DataCallSite(
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
            assignedVariable)
        {
            TransactionWrapper = transactionWrapper,
            IsSynthesized = isSynthesized,
            Confidence = confidence,
            Note = note
        };
    }

    /* ------------------------------------------------------- 트랜잭션 래퍼 */

    /// <param name="Call">래퍼 호출 자체(위치·줄·이름).</param>
    /// <param name="LambdaSpans">그 호출에 인자로 넘어간 람다들의 구문 범위. 이 안의 호출은 트랜잭션 안으로 본다.</param>
    private sealed record WrapperCall(WrapperTransaction Call, IReadOnlyList<Microsoft.CodeAnalysis.Text.TextSpan> LambdaSpans)
    {
        public string Name => Call.Name;
    }

    /// <summary>
    /// 메서드 본문에서 트랜잭션 래퍼 호출을 찾고, 그 호출에 넘어간 람다의 범위를 함께 기록한다.
    /// 설정 패턴(<c>db.transactionWrappers</c>)이나 자동으로 찾은 래퍼 이름에 맞아야 한다.
    /// </summary>
    private static IReadOnlyList<WrapperCall> CollectWrapperCalls(
        SyntaxNode body,
        SemanticModel model,
        TraceLensConfig config,
        IReadOnlySet<string> autoWrappers,
        CancellationToken cancellationToken)
    {
        var result = new List<WrapperCall>();
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lambdas = invocation.ArgumentList.Arguments
                .Select(argument => argument.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .Select(lambda => lambda.Span)
                .ToArray();
            if (lambdas.Length == 0)
            {
                continue;
            }

            var symbolInfo = model.GetSymbolInfo(invocation, cancellationToken);
            if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol called)
            {
                continue;
            }

            var method = called.ReducedFrom ?? called;
            var fullName = $"{method.ContainingType?.ToDisplayString() ?? string.Empty}.{method.Name}";
            if (!PatternMatch.MatchesAny(fullName, config.Db.TransactionWrappers) && !autoWrappers.Contains(fullName))
            {
                continue;
            }

            result.Add(new WrapperCall(
                new WrapperTransaction(
                    invocation.SpanStart,
                    invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    SymbolNames.GetDisplayName(method)),
                lambdas));
        }

        return result;
    }

    /// <summary>이 호출이 트랜잭션 래퍼에 넘긴 람다 안에 있으면 그 래퍼를 돌려준다. 가장 안쪽 래퍼가 이긴다.</summary>
    private static WrapperCall? FindEnclosingWrapper(SyntaxNode node, IReadOnlyList<WrapperCall> wrappers)
    {
        WrapperCall? best = null;
        foreach (var wrapper in wrappers)
        {
            foreach (var span in wrapper.LambdaSpans)
            {
                if (span.Contains(node.SpanStart) && (best is null || span.Length < best.LambdaSpans.Max(item => item.Length)))
                {
                    best = wrapper;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// 솔루션 안에서 "본문에 <c>BeginTransaction</c>이 있고 대리자 파라미터를 직접 부르는" 메서드를 래퍼로 본다.
    /// 설정 패턴에 없어도 <c>GameDbBase.TransactionAsync(Func&lt;…&gt;)</c> 같은 사내 관용 형태를 잡기 위한 것이다.
    /// </summary>
    private static async Task<IReadOnlySet<string>> DetectTransactionWrappersAsync(
        LoadedSolution loaded, CancellationToken cancellationToken)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in loaded.Solution.Projects.Where(project => loaded.AnalyzableProjectIds.Contains(project.Id)))
        {
            foreach (var document in project.Documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = await document.GetTextAsync(cancellationToken);
                // 전체 문서를 다시 파싱하지 않도록 텍스트로 먼저 거른다. 래퍼는 드물다.
                if (!text.ToString().Contains("BeginTransaction", StringComparison.Ordinal))
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
                    if (model.GetDeclaredSymbol(methodSyntax, cancellationToken) is not IMethodSymbol symbol)
                    {
                        continue;
                    }

                    if (!symbol.Parameters.Any(parameter => parameter.Type.TypeKind == TypeKind.Delegate))
                    {
                        continue;
                    }

                    SyntaxNode? methodBody = (SyntaxNode?)methodSyntax.Body ?? methodSyntax.ExpressionBody;
                    if (methodBody is null || !HasTransactionBegin(methodBody) || !InvokesDelegateParameter(methodBody, symbol))
                    {
                        continue;
                    }

                    found.Add($"{symbol.ContainingType?.ToDisplayString() ?? string.Empty}.{symbol.Name}");
                }
            }
        }

        return found;
    }

    private static bool HasTransactionBegin(SyntaxNode body) => body.DescendantNodes()
        .OfType<InvocationExpressionSyntax>()
        .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax member &&
                           member.Name.Identifier.Text.StartsWith("BeginTransaction", StringComparison.Ordinal));

    /// <summary><c>action(…)</c> 또는 <c>action.Invoke(…)</c>처럼 대리자 파라미터를 본문에서 부르는지.</summary>
    private static bool InvokesDelegateParameter(SyntaxNode body, IMethodSymbol symbol)
    {
        var delegateParameters = symbol.Parameters
            .Where(parameter => parameter.Type.TypeKind == TypeKind.Delegate)
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (delegateParameters.Count == 0)
        {
            return false;
        }

        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                MemberAccessExpressionSyntax { Name.Identifier.Text: "Invoke" } member =>
                    (member.Expression as IdentifierNameSyntax)?.Identifier.Text,
                _ => null
            };
            if (name is not null && delegateParameters.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>인터페이스 호출은 DI 등록으로 구현체까지 연결해야 호출 그래프의 노드 id와 맞는다.</summary>
    private IMethodSymbol ResolveCallee(IMethodSymbol method) =>
        method.ContainingType?.TypeKind == TypeKind.Interface ? diResolver.Resolve(method).Method ?? method : method;

    /// <summary>메서드 안의 <c>.Result</c>/<c>.Wait()</c>를 동기 대기 자리로 모은다.</summary>
    private static IEnumerable<DataCallSite> EnumerateSyncWaits(SyntaxNode body)
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
            yield return new DataCallSite(
                node.SpanStart, line, text, text, null, node.ToString(), [], SiteKind.SyncWait,
                string.Empty, string.Empty, AccessOperation.Other, node.ToString().Trim(),
                false, false, false, false, false, false, false, null, false, null);
        }
    }

    private static IReadOnlyList<FailurePath> CollectFailurePaths(SyntaxNode body, IReadOnlyList<DataCallSite> sites)
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
        HashSet<(string File, int Line, string Target, AccessOperation Operation)> seen,
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
            var target = site.Target.Length > 0 ? site.Target : site.Name;
            // 같은 (파일, 줄, 대상, 연산)은 한 번만 남긴다. 같은 자리를 두 경로로 잡으면 목록에 두 번 나온다.
            if (!seen.Add((facts.File, site.Line, target, site.Operation)))
            {
                continue;
            }

            dataSourceKinds.TryAdd(dataSource, site.Kind == SiteKind.Cache ? "cache" : "single");
            accesses.Add(new DataAccess(
                $"{facts.NodeId}#{site.Line}#{accesses.Count}",
                site.Kind == SiteKind.Cache ? StorageKind.Cache : StorageKind.Database,
                dataSource,
                target,
                site.Operation,
                site.Statement,
                facts.NodeId,
                facts.DisplayName,
                facts.ClassId,
                facts.File,
                site.Line,
                site.InTransaction,
                site.InLoop)
            {
                Confidence = site.Confidence,
                Note = site.Note,
                TransactionWrapper = site.TransactionWrapper ?? string.Empty
            });
        }
    }

    private static void CollectTransaction(MethodDataFacts facts, List<TransactionScopeInfo> transactions)
    {
        // \ub798\ud37c\uc5d0 \ub118\uae34 \ub78c\ub2e4\ub3c4 \ud558\ub098\uc758 \ud2b8\ub79c\uc7ad\uc158 \uacbd\uacc4\ub2e4. \uacbd\uacc4\ub294 \ub798\ud37c \uc548\uc5d0 \uc788\uc5b4 Begin/Commit\uc774 \uc774 \uba54\uc11c\ub4dc\uc5d0\ub294 \ubcf4\uc774\uc9c0 \uc54a\ub294\ub2e4.
        foreach (var wrapper in facts.Wrappers)
        {
            var scoped = facts.Sites.Where(site => site.TransactionWrapper == wrapper.Name).ToArray();
            transactions.Add(new TransactionScopeInfo(
                $"tx:{facts.NodeId}#{wrapper.Line}",
                facts.NodeId,
                facts.DisplayName,
                facts.ClassId,
                facts.File,
                wrapper.Line,
                true,
                scoped.Where(site => site.Kind == SiteKind.ConnectionOpen)
                    .Select(site => site.DataSource)
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                [],
                scoped.Where(site => site.Kind == SiteKind.External)
                    .Select(site => $"{site.Statement} ({site.Line}\uc904)").ToArray(),
                scoped.Where(site => site.Kind == SiteKind.Cache)
                    .Select(site => $"{site.Statement} ({site.Line}\uc904)").ToArray())
            {
                Kind = TransactionScopeKind.Wrapper,
                WrapperName = wrapper.Name
            });
        }

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

    /// <summary>
    /// 캐시 클라이언트 호출인지. 기본 패턴 <c>*Cache.*</c>는 솔루션 안 래퍼 타입(<c>…RankCache</c>)에도 걸리므로,
    /// <b>솔루션 안에 선언된 메서드는 캐시 API로 보지 않는다</b>. 래퍼 본문에서 실제 클라이언트 API를 만나는 곳에서만 캐시 자리를 만든다.
    /// 호출자는 접근 요약 전파로 래퍼의 캐시 접근을 물려받으므로 쓰기 경로에는 계속 나타난다.
    /// 내부 래퍼를 굳이 클라이언트로 보고 싶으면 <c>db.cacheWrappers</c>에 적는다.
    /// </summary>
    private static bool IsCacheApi(
        string calleeSymbol,
        INamedTypeSymbol? containingType,
        DataAccessOptions options,
        bool isSolutionMethod)
    {
        if (containingType is null)
        {
            return false;
        }

        if (PatternMatch.MatchesAny(calleeSymbol, options.CacheWrappers))
        {
            return true;
        }

        if (isSolutionMethod)
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

    /// <summary>
    /// <c>CommandType.StoredProcedure</c>를 인자(위치 또는 <c>commandType:</c> 명명 인자)에서 찾는다.
    /// Dapper는 이 값이 있으면 첫 인자를 SQL이 아니라 <b>프로시저 이름</b>으로 읽는다.
    /// </summary>
    private static bool IsStoredProcedureCall(
        InvocationExpressionSyntax invocation,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var argument in arguments)
        {
            if (argument.Expression is not MemberAccessExpressionSyntax member ||
                member.Name.Identifier.ValueText != "StoredProcedure")
            {
                continue;
            }

            var type = model.GetTypeInfo(member.Expression, cancellationToken).Type
                ?? (model.GetSymbolInfo(member.Expression, cancellationToken).Symbol as INamedTypeSymbol);
            if (type?.Name == "CommandType")
            {
                return true;
            }
        }

        return false;
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

    /// <summary>
    /// 만료 인자가 있는지. <c>TimeSpan?</c>(Nullable)과 명명 인자(<c>expiry:</c>),
    /// <c>CommandFlags</c> 뒤에 오는 만료 인자까지 본다. 인자 순서를 가정하지 않고 전부 훑는다.
    /// </summary>
    private static bool HasExpiryArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var argument in arguments)
        {
            var name = argument.NameColon?.Name.Identifier.Text;
            if (name is not null &&
                (name.Contains("expir", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("ttl", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("absolute", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("sliding", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (IsExpiryType(model.GetTypeInfo(argument.Expression, cancellationToken).Type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>TimeSpan</c>·<c>DateTimeOffset</c> 등과 그 Nullable 형태를 만료 값으로 본다.</summary>
    private static bool IsExpiryType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable &&
            nullable.TypeArguments.Length == 1)
        {
            type = nullable.TypeArguments[0];
        }

        return type.Name is "TimeSpan" or "DateTimeOffset" or "DateTime" or "DistributedCacheEntryOptions"
            or "MemoryCacheEntryOptions";
    }

    /// <param name="Text">화면에 보여줄 대상(캐시 키 또는 "필드 x(추정)").</param>
    /// <param name="Certain">키를 이 자리에서 확정했는지.</param>
    internal sealed record CacheTarget(string Text, bool Certain, string Note);

    /// <summary>
    /// 캐시 대상을 읽는다. 상수·보간 문자열이면 그대로 키다.
    /// CloudStructures처럼 키가 클라이언트 생성자에 있고 호출에는 값만 넘기는 API는 변수 이름을 키로 쓰지 않고,
    /// 수신 필드 이름을 "필드 x(추정)"로 적는다.
    /// </summary>
    private static CacheTarget ReadCacheTarget(
        InvocationExpressionSyntax invocation,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (arguments.Count > 0)
        {
            var first = arguments[0].Expression;
            var constant = model.GetConstantValue(first, cancellationToken);
            if (constant.HasValue && constant.Value is string literal && literal.Length > 0)
            {
                return new CacheTarget(literal.Trim(), true, string.Empty);
            }

            if (first is InterpolatedStringExpressionSyntax)
            {
                var (text, _) = ReadStringArgument([arguments[0]], model, cancellationToken);
                if (text is not null)
                {
                    return new CacheTarget(text, true, string.Empty);
                }
            }

            // 첫 인자가 문자열이면 그 API에서는 그것이 키다. 값을 모르니 변수 이름을 "{name}" 꼴로만 적는다.
            var firstType = model.GetTypeInfo(first, cancellationToken).Type;
            if (firstType?.SpecialType == SpecialType.System_String && GetIdentifier(first) is { } keyName)
            {
                return new CacheTarget(
                    "{" + keyName + "}",
                    false,
                    "키를 이 자리에서 확정하지 못했다. 호출자가 넘기는 값이다.");
            }
        }

        // CloudStructures처럼 키가 클라이언트 생성 시점에 정해지고 호출에는 값만 넘기는 API다.
        var receiver = invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? GetIdentifier(memberAccess.Expression)
            : null;
        return receiver is null
            ? new CacheTarget("?", false, "키를 이 자리에서 확정하지 못했다.")
            : new CacheTarget(
                $"필드 {receiver}(추정)",
                false,
                $"키가 호출 인자에 없다. 클라이언트 {receiver}를 만들 때 정해진 키로 본다.");
    }

    /// <summary>terminal 호출의 인자 중 <c>IDbTransaction</c> 파생이 있으면 트랜잭션 안이다.</summary>
    private static bool HasTransactionArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var argument in arguments)
        {
            var type = model.GetTypeInfo(argument.Expression, cancellationToken).Type;
            if (IsDbTransaction(type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDbTransaction(ITypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name is "DbTransaction" or "IDbTransaction")
            {
                return true;
            }
        }

        return type?.AllInterfaces.Any(item => item.Name == "IDbTransaction") ?? false;
    }

    /* ------------------------------------------------------- 쿼리 빌더(SqlKata 등) */

    /// <param name="TableExpression">테이블 인자의 원문. 상수가 아닐 때 근거로 남긴다.</param>
    internal sealed record QueryBuilderSite(
        string Table,
        bool TableCertain,
        string TableExpression,
        AccessOperation Operation,
        string Statement);

    /// <summary>
    /// <c>db.Query("t").Where(…).UpdateAsync(…)</c> 같은 빌더 체인의 <b>실행 지점</b>을 DB 접근으로 인식한다.
    /// terminal 이름이 설정에 있고 수신식 타입이 빌더의 <c>queryType</c>일 때만 후보로 보고,
    /// 체인을 거슬러 올라가 <c>root</c> 호출에서 테이블 이름을 읽는다.
    /// </summary>
    private static QueryBuilderSite? ResolveQueryBuilderSite(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel model,
        DataAccessOptions options,
        CancellationToken cancellationToken)
    {
        if (options.QueryBuilders.Count == 0 || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        foreach (var builder in options.QueryBuilders)
        {
            if (!builder.Terminals.TryGetValue(method.Name, out var operationName))
            {
                continue;
            }

            var receiverType = model.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            if (!IsOrDerivesFrom(receiverType, builder.QueryType))
            {
                continue;
            }

            var joins = new List<string>();
            var root = FindBuilderRoot(memberAccess.Expression, model, builder, joins, cancellationToken);
            var operation = operationName switch
            {
                AccessOperationName.Read => AccessOperation.Read,
                AccessOperationName.Write => AccessOperation.Write,
                AccessOperationName.Delete => AccessOperation.Delete,
                _ => AccessOperation.Other
            };
            var verb = operation switch
            {
                AccessOperation.Read => "SELECT",
                AccessOperation.Delete => "DELETE",
                AccessOperation.Write => method.Name.StartsWith("Insert", StringComparison.OrdinalIgnoreCase)
                    ? "INSERT"
                    : "UPDATE",
                _ => "QUERY"
            };

            string table;
            var certain = false;
            var expression = string.Empty;
            if (root is null)
            {
                table = "?";
                expression = memberAccess.Expression.ToString();
            }
            else
            {
                var argumentList = root.ArgumentList.Arguments;
                if (argumentList.Count <= builder.TableArgumentIndex)
                {
                    table = "?";
                    expression = root.ToString();
                }
                else
                {
                    var argument = argumentList[builder.TableArgumentIndex].Expression;
                    expression = argument.ToString();
                    var constant = model.GetConstantValue(argument, cancellationToken);
                    if (constant.HasValue && constant.Value is string name && name.Length > 0)
                    {
                        table = name;
                        certain = true;
                    }
                    else
                    {
                        table = "?";
                    }
                }
            }

            var statement = joins.Count == 0
                ? $"{verb} {table}"
                : $"{verb} {table} JOIN {string.Join(", ", joins)}";
            return new QueryBuilderSite(table, certain, expression, operation, statement);
        }

        return null;
    }

    /// <summary>
    /// 빌더 체인을 거슬러 올라가 <c>root</c> 호출을 찾는다. 지역 변수에 담긴 체인은 같은 메서드 안 선언자의 초기화식을 한 단계 따라간다.
    /// 지나치는 join 메서드의 상수 테이블 이름은 읽기 대상으로 모은다.
    /// </summary>
    private static InvocationExpressionSyntax? FindBuilderRoot(
        ExpressionSyntax start,
        SemanticModel model,
        QueryBuilderOption builder,
        List<string> joins,
        CancellationToken cancellationToken)
    {
        var expression = start;
        var followedVariable = false;
        for (var step = 0; step < 64 && expression is not null; step++)
        {
            switch (expression)
            {
                case InvocationExpressionSyntax invocation:
                {
                    var symbolInfo = model.GetSymbolInfo(invocation, cancellationToken);
                    if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is IMethodSymbol called)
                    {
                        var reduced = called.ReducedFrom ?? called;
                        var fullName = $"{reduced.ContainingType?.ToDisplayString() ?? string.Empty}.{reduced.Name}";
                        if (PatternMatch.Matches(fullName, builder.Root))
                        {
                            return invocation;
                        }

                        if (builder.JoinMethods.Contains(reduced.Name, StringComparer.OrdinalIgnoreCase) &&
                            invocation.ArgumentList.Arguments.Count > 0)
                        {
                            var constant = model.GetConstantValue(
                                invocation.ArgumentList.Arguments[0].Expression, cancellationToken);
                            if (constant.HasValue && constant.Value is string joined && joined.Length > 0)
                            {
                                joins.Add(joined);
                            }
                        }
                    }

                    expression = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
                    continue;
                }

                case MemberAccessExpressionSyntax member:
                    expression = member.Expression;
                    continue;

                case IdentifierNameSyntax identifier when !followedVariable:
                {
                    followedVariable = true;
                    expression = FindLocalInitializer(identifier, model, cancellationToken);
                    continue;
                }

                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>같은 메서드 안에서 이 식별자를 선언한 지역 변수의 초기화식을 돌려준다(한 단계만 따라간다).</summary>
    private static ExpressionSyntax? FindLocalInitializer(
        IdentifierNameSyntax identifier, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return null;
        }

        var reference = local.DeclaringSyntaxReferences.FirstOrDefault();
        return reference?.GetSyntax(cancellationToken) is VariableDeclaratorSyntax declarator
            ? declarator.Initializer?.Value
            : null;
    }

    private static bool IsOrDerivesFrom(ITypeSymbol? type, string fullName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.OriginalDefinition.ToDisplayString(), fullName, StringComparison.Ordinal))
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

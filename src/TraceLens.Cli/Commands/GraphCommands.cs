using System.Text.Json;
using TraceLens.Context;

namespace TraceLens.Cli.Commands;

/// <summary>
/// 서버를 띄우지 않고 저장된 분석 결과만 읽는 명령들(T63).
/// "PR에서 바뀐 메서드의 영향 진입점을 코멘트로 단다" 같은 자동화가 UI 없이 돌아야 한다.
///
/// 모두 <see cref="TraceLens.Context"/> 라이브러리만 쓴다 — 분석은 하지 않는다(CLAUDE.md 제약 3).
/// </summary>
public static class GraphCommands
{
    /// <summary>종료 코드. README·`--help`·계약 문서가 같은 표를 쓴다.</summary>
    public const int Success = 0;
    public const int Failure = 1;
    public const int BadArguments = 2;

    /// <summary>비교 전제 조건이 다르다(대상·분석기·규칙·범위). 결과를 "수정 완료"의 근거로 쓰면 안 된다.</summary>
    public const int PreconditionMismatch = 3;

    /// <summary>그래프가 현재 소스와 다르다(stale). 결과는 옛 코드 기준이다.</summary>
    public const int StaleGraph = 4;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// <c>tracelens impact &lt;대상&gt; --node &lt;id|"Class.Method"&gt; [--changed-files a.cs,b.cs]</c>.
    /// 바뀐 메서드에서 영향을 받을 수 있는 진입점 후보를 찾는다. 안전 판정이 아니다.
    /// </summary>
    public static async Task<int> ImpactAsync(string[] args, TextWriter output, TextWriter error)
    {
        var loaded = await LoadAsync(args, error);
        if (loaded.ExitCode != Success)
        {
            return loaded.ExitCode;
        }

        var index = loaded.Index!;
        var nodeIds = ResolveTargets(index, args);
        if (nodeIds.Count == 0)
        {
            error.WriteLine("--node나 --changed-files로 대상을 지정해야 한다.");
            return BadArguments;
        }

        var result = new ImpactAnalyzer().Analyze(
            index,
            new ImpactRequest(nodeIds, TimeoutMs: Options.Int(args, "--timeout-ms") ?? 5000));

        if (Options.Has(args, "--json"))
        {
            output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            output.WriteLine($"대상 {result.Targets.Count}개 · 영향 후보 진입점 {result.TotalEndpoints}개");
            foreach (var endpoint in result.Endpoints)
            {
                output.WriteLine($"  {endpoint.HttpMethod,-6} {endpoint.Route}  (거리 {endpoint.Distance}, {endpoint.Confidence})");
            }

            foreach (var write in result.DataWrites)
            {
                output.WriteLine($"  쓰기: {write.Target} {write.Operation} — {write.File}:{write.Line}");
            }

            foreach (var line in result.Limitations.Concat(result.TruncationReasons))
            {
                output.WriteLine($"  · {line}");
            }
        }

        return loaded.Stale ? StaleGraph : Success;
    }

    /// <summary>
    /// <c>tracelens context &lt;대상&gt; --node … --intent … --budget … [--markdown|--json]</c>.
    /// 클립보드 대신 표준 출력으로 문맥을 낸다.
    /// </summary>
    public static async Task<int> ContextAsync(string[] args, TextWriter output, TextWriter error)
    {
        var loaded = await LoadAsync(args, error);
        if (loaded.ExitCode != Success)
        {
            return loaded.ExitCode;
        }

        var index = loaded.Index!;
        var kindText = Options.Value(args, "--kind") ?? "node";
        if (!Enum.TryParse<SelectionKind>(kindText, ignoreCase: true, out var kind))
        {
            error.WriteLine($"지원하지 않는 선택 종류다: {kindText}. 쓸 수 있는 값: {string.Join(", ", Enum.GetNames<SelectionKind>())}");
            return BadArguments;
        }

        var id = Options.Value(args, "--node") ?? Options.Value(args, "--id");
        if (string.IsNullOrWhiteSpace(id))
        {
            error.WriteLine("--node(또는 --id)가 필요하다.");
            return BadArguments;
        }

        if (kind == SelectionKind.Node)
        {
            id = ResolveNodeId(index, id) ?? id;
        }

        try
        {
            var document = await new ContextBuilder(new GraphFreshnessEvaluator()).BuildAsync(
                index,
                loaded.Source!,
                new ContextRequest(
                    new ContextSelection(kind, id),
                    Parse(Options.Value(args, "--intent"), ContextIntent.Explain),
                    Parse(Options.Value(args, "--budget"), ContextBudget.Balanced))
                {
                    Question = Options.Value(args, "--question") ?? string.Empty
                });

            output.WriteLine(Options.Has(args, "--json")
                ? JsonSerializer.Serialize(document, JsonOptions)
                : document.Markdown);
            return loaded.Stale ? StaleGraph : Success;
        }
        catch (ContextException exception)
        {
            error.WriteLine($"{exception.Code}: {exception.Message}");
            return exception.Code == ContextErrorCodes.SelectionNotFound ? Failure : BadArguments;
        }
    }

    /// <summary>
    /// <c>tracelens compare &lt;대상&gt; --baseline &lt;snapshotId|latest&gt; [--json]</c>.
    /// 전제 조건이 다르면 종료 코드 3으로 알린다 — "차이 없음"을 안전으로 읽지 않게 한다.
    /// </summary>
    public static async Task<int> CompareAsync(string[] args, TextWriter output, TextWriter error)
    {
        var loaded = await LoadAsync(args, error);
        if (loaded.ExitCode != Success)
        {
            return loaded.ExitCode;
        }

        var graph = loaded.Index!.Graph;
        var baselineId = Options.Value(args, "--baseline");
        if (string.IsNullOrWhiteSpace(baselineId))
        {
            error.WriteLine("--baseline <snapshotId|latest>가 필요하다. `tracelens compare <대상> --list`로 목록을 본다.");
            return BadArguments;
        }

        var store = new SnapshotStore();
        var entries = await store.ListAsync(graph);
        if (Options.Has(args, "--list"))
        {
            foreach (var entry in entries)
            {
                output.WriteLine($"{entry.SnapshotId}  {entry.SavedAt:yyyy-MM-dd HH:mm}  {entry.Label}");
            }

            return Success;
        }

        if (string.Equals(baselineId, "latest", StringComparison.OrdinalIgnoreCase))
        {
            baselineId = entries.OrderByDescending(entry => entry.SavedAt).FirstOrDefault()?.SnapshotId;
            if (baselineId is null)
            {
                error.WriteLine("보관한 기준 스냅샷이 없다. 화면에서 기준을 저장한 뒤 다시 실행한다.");
                return Failure;
            }
        }

        var baseline = await store.LoadAsync(graph, baselineId);
        if (!baseline.Ok || baseline.Graph is null)
        {
            error.WriteLine($"기준 스냅샷을 읽을 수 없다: {baselineId}");
            return Failure;
        }

        var diff = new SnapshotComparer().Compare(baseline.Graph, graph);
        if (Options.Has(args, "--json"))
        {
            output.WriteLine(JsonSerializer.Serialize(diff, JsonOptions));
        }
        else
        {
            var counts = diff.Counts;
            output.WriteLine($"기준 {diff.BaselineId} → 현재 {diff.CurrentId}");
            output.WriteLine(
                $"  진입점 +{counts.EndpointsAdded}/-{counts.EndpointsRemoved} · " +
                $"메서드 +{counts.MethodsAdded}/-{counts.MethodsRemoved}/~{counts.MethodsChanged} · " +
                $"엣지 +{counts.EdgesAdded}/-{counts.EdgesRemoved} · " +
                $"점검 +{counts.FindingsAdded}/-{counts.FindingsResolved}");
            output.WriteLine($"  미들웨어 {diff.Middleware.Count}건 · 시작 흐름 {diff.StartupFlow.Count}건 · 쓰기 경로 {diff.WritePaths.Count}건");
            foreach (var line in diff.NotCompared)
            {
                output.WriteLine($"  비교하지 않음: {line}");
            }

            foreach (var line in diff.Preconditions.Differences)
            {
                output.WriteLine($"  전제 조건 다름: {line}");
            }
        }

        // 전제 조건이 다르면 "차이 없음"을 안전으로 읽으면 안 된다. 종료 코드로 구분한다.
        return diff.Preconditions.Ok ? loaded.Stale ? StaleGraph : Success : PreconditionMismatch;
    }

    private sealed record LoadedGraph(int ExitCode, GraphIndex? Index, SourceReader? Source, bool Stale);

    /// <summary>저장된 graph.json을 읽고 신선도를 확인한다. 재분석은 하지 않는다.</summary>
    private static async Task<LoadedGraph> LoadAsync(string[] args, TextWriter error)
    {
        var target = Options.FirstPositional(args);
        if (string.IsNullOrWhiteSpace(target))
        {
            error.WriteLine("분석 대상(.sln/.slnx/.csproj) 또는 그 폴더 경로가 필요하다.");
            return new LoadedGraph(BadArguments, null, null, false);
        }

        var location = await GraphLocator.FindForTargetAsync(target);
        if (!location.Found)
        {
            error.WriteLine($"분석 결과를 찾지 못했다: {target}. 먼저 `tracelens analyze {target}`을 실행한다.");
            foreach (var searched in location.SearchedPaths)
            {
                error.WriteLine($"  찾아본 곳: {searched}");
            }

            return new LoadedGraph(Failure, null, null, false);
        }

        var graphPath = location.GraphPath!;

        var loaded = await GraphJsonStore.LoadAsync(graphPath);
        if (!loaded.Ok || loaded.Graph is null)
        {
            error.WriteLine($"그래프 파일을 읽을 수 없다: {graphPath}");
            foreach (var issue in loaded.Issues)
            {
                error.WriteLine($"  {issue.Code}: {issue.Message}");
            }

            return new LoadedGraph(Failure, null, null, false);
        }

        var graph = loaded.Graph;
        var freshness = await new GraphFreshnessEvaluator().EvaluateAsync(graph);
        var stale = freshness.Status == FreshnessStatus.Stale;
        if (stale)
        {
            error.WriteLine(
                $"분석 결과가 현재 소스와 다르다(stale, 바뀐 파일 {freshness.ChangedFiles.Count}개). " +
                "결과는 옛 코드 기준이다. 재분석은 사용자가 실행한다.");
        }

        return new LoadedGraph(
            Success,
            new GraphIndex(graph),
            new SourceReader(graph, SourceAccessPolicy.ForGraph(graph)),
            stale);
    }

    /// <summary>`--node`와 `--changed-files`를 노드 ID 목록으로 바꾼다.</summary>
    private static IReadOnlyList<string> ResolveTargets(GraphIndex index, string[] args)
    {
        var ids = new List<string>();
        foreach (var value in Options.Values(args, "--node"))
        {
            var resolved = ResolveNodeId(index, value);
            if (resolved is not null)
            {
                ids.Add(resolved);
            }
        }

        var files = Options.Value(args, "--changed-files");
        if (!string.IsNullOrWhiteSpace(files))
        {
            var wanted = files
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .ToArray();
            ids.AddRange(index.Graph.Nodes
                .Where(node => wanted.Any(file => SameFile(node.File, file)))
                .Select(node => node.Id));
        }

        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    /// <summary>
    /// 그래프의 노드 경로는 분석 루트 기준이고 `git diff --name-only`는 저장소 루트 기준이라
    /// 어느 쪽이 더 긴 경로인지 알 수 없다. 양쪽 모두 경로 경계에서 꼬리가 맞으면 같은 파일로 본다.
    /// 경계를 보지 않으면 `Order.cs`가 `MyOrder.cs`와도 맞는다.
    /// </summary>
    private static bool SameFile(string nodeFile, string changedFile)
    {
        var node = Normalize(nodeFile);
        var changed = Normalize(changedFile);
        return EndsWithPathSegment(node, changed) || EndsWithPathSegment(changed, node);
    }

    private static bool EndsWithPathSegment(string path, string suffix)
    {
        if (suffix.Length == 0 ||
            path.Length < suffix.Length ||
            !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Length == suffix.Length || path[path.Length - suffix.Length - 1] == '/';
    }

    /// <summary>노드 ID를 그대로 받거나 `Class.Method` 같은 표시 이름으로 찾는다.</summary>
    private static string? ResolveNodeId(GraphIndex index, string value)
    {
        if (index.FindNode(value) is not null)
        {
            return value;
        }

        var matches = index.Graph.Nodes
            .Where(node => node.DisplayName.StartsWith(value, StringComparison.OrdinalIgnoreCase) ||
                           node.Id.Contains(value, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1 ? matches[0].Id : matches.FirstOrDefault()?.Id;
    }

    private static TEnum Parse<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}

/// <summary>명령줄 인자를 읽는 작은 도우미. CommandRouter의 것과 같은 규칙을 쓴다.</summary>
internal static class Options
{
    /// <summary>값을 받지 않는 옵션. 그 뒤의 토큰은 옵션 값이 아니다.</summary>
    private static readonly string[] BooleanFlags = ["--json", "--markdown", "--list"];

    /// <summary>
    /// 옵션이 아닌 첫 인자(분석 대상)를 찾는다.
    /// 옵션의 <b>값</b>을 대상으로 착각하면 안 된다 — `tracelens impact --node X`는 대상을 안 준 것인데
    /// `X`를 경로로 읽으면 인자 오류(2)가 "찾지 못했다"(1)로 바뀐다.
    /// </summary>
    public static string? FirstPositional(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var item = args[index];
            if (!item.StartsWith('-'))
            {
                return item;
            }

            if (!BooleanFlags.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                index++;
            }
        }

        return null;
    }

    public static string? Value(string[] args, string name)
    {
        var index = Array.FindIndex(args, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>같은 옵션을 여러 번 줄 수 있다(`--node A --node B`).</summary>
    public static IReadOnlyList<string> Values(string[] args, string name)
    {
        var values = new List<string>();
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                values.Add(args[index + 1]);
            }
        }

        return values;
    }

    public static int? Int(string[] args, string name) =>
        int.TryParse(Value(args, name), out var value) ? value : null;

    public static bool Has(string[] args, string name) =>
        args.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
}

using System.Globalization;
using System.Text;

namespace TraceLens.Context;

/// <summary>
/// <see cref="ContextDocument"/> 하나에서 Markdown을 만든다.
/// UI 클립보드·인앱 실행·MCP가 모두 이 결과를 쓰므로 어디서 복사해도 같은 문맥이 나온다.
/// </summary>
public static class ContextMarkdown
{
    public static string Render(ContextDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# TraceLens 문맥");
        builder.AppendLine();

        // (1) 목적·질문·선택한 코드의 식별자
        builder.AppendLine($"- 작업 목적: {DescribeIntent(document.Intent)}");
        builder.AppendLine($"- 선택: {document.Selection.Kind} · `{document.Selection.Id}`");
        if (!string.IsNullOrWhiteSpace(document.Question))
        {
            builder.AppendLine($"- 질문: {document.Question.Trim()}");
        }

        builder.AppendLine();
        builder.AppendLine(document.Summary);
        builder.AppendLine();

        // (2) 분석 시각·스냅샷·신선도·범위
        builder.AppendLine("## 분석 상태");
        builder.AppendLine();
        builder.AppendLine($"- 분석 시각: {document.Analysis.AnalyzedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- snapshotId: `{Or(document.SnapshotId, "(없음)")}` · inputFingerprint: `{Short(document.InputFingerprint)}`");
        builder.AppendLine($"- 신선도: {document.Freshness.Status}{FormatChangedFiles(document)}");
        builder.AppendLine($"- 분석 범위: {document.Analysis.AnalysisScope} · 프로젝트 {document.Analysis.LoadedProjects}/{document.Analysis.TotalProjects}" +
                           (document.Analysis.ProjectsWithCompileErrors > 0
                               ? $" (컴파일 오류 {document.Analysis.ProjectsWithCompileErrors}개)"
                               : string.Empty));
        if (document.Analysis.InspectedCallSites > 0)
        {
            builder.AppendLine(
                $"- 호출 해석률: {document.Analysis.ResolvedCallSites}/{document.Analysis.InspectedCallSites} " +
                $"({document.Analysis.ResolutionRate:P1}) — 분모는 솔루션 안 대상을 부른 호출 지점이다. 호출 \"정확도\"와는 다른 값이다.");
        }

        builder.AppendLine();

        // (3) 근거와 소스
        if (document.Evidence.Count > 0)
        {
            builder.AppendLine("## 근거");
            builder.AppendLine();
            builder.AppendLine("| id | 종류 | 위치 | 판정 | 내용 |");
            builder.AppendLine("|---|---|---|---|---|");
            foreach (var item in document.Evidence)
            {
                var location = item.StartLine == item.EndLine
                    ? $"{item.File}:{item.StartLine}"
                    : $"{item.File}:{item.StartLine}-{item.EndLine}";
                var lambdaMark = item.ViaLambda ? " · 람다 본문" : string.Empty;
                builder.AppendLine(
                    $"| `{item.Id}` | {item.Kind} | {location} | {item.Resolution}/{item.Confidence}{lambdaMark} | {Escape(item.Detail)} |");
            }

            builder.AppendLine();
        }

        if (document.SourceSnippets.Count > 0)
        {
            builder.AppendLine("## 소스");
            builder.AppendLine();
            foreach (var snippet in document.SourceSnippets)
            {
                builder.AppendLine($"### {snippet.File}:{snippet.StartLine}-{snippet.EndLine} ({snippet.EvidenceId}, {snippet.SourceVersion})");
                if (snippet.SourceVersion == "current")
                {
                    builder.AppendLine();
                    builder.AppendLine("> 분석 당시와 파일 내용이 다르다. 지금 디스크에 있는 소스다.");
                }

                builder.AppendLine();
                builder.AppendLine("```csharp");
                builder.AppendLine(snippet.Content);
                builder.AppendLine("```");
                if (snippet.Truncated)
                {
                    builder.AppendLine();
                    builder.AppendLine("> 길이 제한으로 잘렸다.");
                }

                builder.AppendLine();
            }
        }

        // (4) 진입점 → 대상 경로
        if (document.Paths.Count > 0)
        {
            builder.AppendLine("## 진입점에서 대상까지의 대표 경로");
            builder.AppendLine();
            foreach (var path in document.Paths)
            {
                builder.AppendLine($"- **{path.HttpMethod} {path.Route}** (`{path.EndpointId}`)");
                foreach (var step in path.Steps)
                {
                    builder.AppendLine(
                        $"  - {step.DisplayName} — {step.File}:{step.StartLine} " +
                        $"({step.Resolution}/{step.Confidence}{(step.ViaLambda ? " · 람다 본문" : string.Empty)})");
                }

                if (path.Truncated)
                {
                    builder.AppendLine("  - (경로가 길어 잘렸다)");
                }
            }

            builder.AppendLine();
        }

        // (5) 인접 호출과 점검
        if (document.Callers.Count > 0 || document.Callees.Count > 0)
        {
            builder.AppendLine("## 인접 호출");
            builder.AppendLine();
            if (document.Callers.Count > 0)
            {
                builder.AppendLine("호출하는 쪽:");
                foreach (var item in document.Callers)
                {
                    builder.AppendLine($"- {item.DisplayName} — {item.File}:{item.StartLine}");
                }

                builder.AppendLine();
            }

            if (document.Callees.Count > 0)
            {
                builder.AppendLine("호출받는 쪽:");
                foreach (var item in document.Callees)
                {
                    builder.AppendLine($"- {item.DisplayName} — {item.File}:{item.StartLine}");
                }

                builder.AppendLine();
            }
        }

        if (document.Findings.Count > 0)
        {
            builder.AppendLine("## 관련 점검");
            builder.AppendLine();
            foreach (var finding in document.Findings)
            {
                builder.AppendLine($"- **{finding.RuleId}** ({finding.Severity}/{finding.Confidence}) {finding.What} — {finding.File}:{finding.Line}");
                if (!string.IsNullOrWhiteSpace(finding.Why))
                {
                    builder.AppendLine($"  - 왜: {finding.Why}");
                }

                if (!string.IsNullOrWhiteSpace(finding.Fix))
                {
                    builder.AppendLine($"  - 제안: {finding.Fix}");
                }
            }

            builder.AppendLine();
        }

        // (6) 테스트 후보
        if (document.TestCandidates.Count > 0)
        {
            builder.AppendLine("## 테스트 후보");
            builder.AppendLine();
            builder.AppendLine("아래는 분석 그래프에서 확인한 후보다. 테스트를 실행한 결과가 아니다.");
            foreach (var item in document.TestCandidates)
            {
                builder.AppendLine($"- {item.DisplayName} — {item.File}:{item.StartLine} ({item.Kind})");
            }

            builder.AppendLine();
        }

        // (7) 한계와 생략한 항목
        builder.AppendLine("## 한계와 생략");
        builder.AppendLine();
        foreach (var limitation in document.Limitations)
        {
            builder.AppendLine($"- {limitation}");
        }

        if (document.Omitted.Nodes > 0)
        {
            builder.AppendLine($"- 메서드 {document.Omitted.Nodes}개를 길이 제한으로 생략했다.");
        }

        foreach (var range in document.Omitted.SourceRanges)
        {
            builder.AppendLine($"- 소스 생략: {range}");
        }

        foreach (var section in document.Omitted.Sections)
        {
            builder.AppendLine($"- 항목 생략: {section}");
        }

        builder.AppendLine("- 더 필요한 근거는 TraceLens MCP의 `get_context`·`get_node`·`get_source`·`get_findings`로 스냅샷을 지정해 조회하면 된다.");
        builder.AppendLine();
        return builder.ToString();
    }

    private static string FormatChangedFiles(ContextDocument document) =>
        document.Freshness.ChangedFiles.Count == 0
            ? string.Empty
            : $" (바뀐 파일 {document.Freshness.ChangedFiles.Count}개: {string.Join(", ", document.Freshness.ChangedFiles.Take(5))})";

    private static string DescribeIntent(string intent) => intent switch
    {
        nameof(ContextIntent.Explain) => "처음 보는 코드를 이해한다",
        nameof(ContextIntent.PlanChange) => "변경을 계획한다",
        nameof(ContextIntent.ReviewChange) => "수정 결과를 검토한다",
        nameof(ContextIntent.InvestigateData) => "DB·캐시 불일치를 조사한다",
        nameof(ContextIntent.Share) => "사람에게 공유한다",
        _ => intent
    };

    private static string Or(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

    private static string Short(string value) =>
        string.IsNullOrEmpty(value) ? "(없음)" : value.Length <= 24 ? value : value[..24] + "…";

    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\n", " ");
}

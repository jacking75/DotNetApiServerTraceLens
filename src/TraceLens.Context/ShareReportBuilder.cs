using System.Globalization;
using System.Text;
using TraceLens.Analyzer.Models;

namespace TraceLens.Context;

public sealed record ShareReportRequest(ContextSelection Selection)
{
    /// <summary>상대 경로도 업무 정보를 담을 수 있으므로 파일 경로를 뺄 수 있게 한다.</summary>
    public bool IncludePaths { get; init; } = true;

    /// <summary>테이블·캐시 키 이름을 뺄 수 있게 한다.</summary>
    public bool IncludeStorageNames { get; init; } = true;

    public string Title { get; init; } = string.Empty;
}

public sealed record ShareReport(string Title, string Markdown, IReadOnlyList<string> Included);

/// <summary>
/// 선택한 범위만 담는 공유용 Markdown 보고서를 만든다.
/// 기본 출력에서 절대 로컬 경로와 소스 본문을 제외하고, 상대 경로·테이블명도 끌 수 있게 한다.
/// 서버를 외부에 공개하는 공유 방식은 만들지 않는다.
/// </summary>
public sealed class ShareReportBuilder
{
    public ShareReport Build(GraphIndex index, ContextDocument context, ShareReportRequest request)
    {
        var graph = index.Graph;
        var included = new List<string>();
        var builder = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"TraceLens 보고서 — {context.Selection.DisplayName}"
            : request.Title.Trim();

        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine($"- 분석 시각: {graph.AnalyzedAt.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- 신선도: {context.Freshness.Status}");
        builder.AppendLine($"- 분석 범위: {context.Analysis.AnalysisScope} · 프로젝트 {context.Analysis.LoadedProjects}/{context.Analysis.TotalProjects}");
        builder.AppendLine($"- 대상: {context.Selection.Kind} · {context.Selection.DisplayName}");
        builder.AppendLine();
        included.Add("분석 시각과 범위");

        builder.AppendLine("## 요약");
        builder.AppendLine();
        builder.AppendLine(context.Summary);
        builder.AppendLine();
        included.Add("요약");

        if (context.Paths.Count > 0)
        {
            builder.AppendLine("## 호출 경로");
            builder.AppendLine();
            foreach (var path in context.Paths)
            {
                builder.AppendLine($"### {path.HttpMethod} {path.Route}");
                builder.AppendLine();
                foreach (var step in path.Steps)
                {
                    var location = request.IncludePaths ? $" — {step.File}:{step.StartLine}" : string.Empty;
                    builder.AppendLine($"1. {step.DisplayName}{location} ({step.Resolution}/{step.Confidence})");
                }

                builder.AppendLine();
            }

            included.Add("호출 경로");
        }

        if (context.Findings.Count > 0)
        {
            builder.AppendLine("## 점검");
            builder.AppendLine();
            builder.AppendLine("| 규칙 | 심각도 | 확실도 | 내용 |");
            builder.AppendLine("|---|---|---|---|");
            foreach (var finding in context.Findings)
            {
                var what = request.IncludeStorageNames ? finding.What : MaskStorageNames(finding.What);
                builder.AppendLine($"| {finding.RuleId} | {finding.Severity} | {finding.Confidence} | {what} |");
            }

            builder.AppendLine();
            included.Add("점검 결과");
        }

        builder.AppendLine("## 한계");
        builder.AppendLine();
        foreach (var limitation in context.Limitations)
        {
            builder.AppendLine($"- {limitation}");
        }

        builder.AppendLine("- 이 보고서에는 소스 본문과 절대 경로가 들어 있지 않다.");
        builder.AppendLine();
        included.Add("한계 설명");

        return new ShareReport(title, builder.ToString(), included);
    }

    /// <summary>테이블·키 이름을 뺄 때 쓰는 아주 단순한 가림. 정교한 비밀값 탐지가 아니다.</summary>
    private static string MaskStorageNames(string value)
    {
        var parts = value.Split(' ');
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length > 3 && parts[index].All(character => char.IsLetterOrDigit(character) || character == '_') &&
                parts[index].Any(char.IsUpper))
            {
                parts[index] = "(가림)";
            }
        }

        return string.Join(' ', parts);
    }
}

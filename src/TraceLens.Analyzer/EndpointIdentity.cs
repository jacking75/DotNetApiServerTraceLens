using System.Text;

namespace TraceLens.Analyzer;

/// <summary>
/// 엔드포인트 ID와 정규화한 라우트를 만든다.
/// ID는 <c>projectKey|httpMethod|normalizedRoute|registrationIdentity</c>로, 의미적 등록 식별자를 우선한다.
/// 파일 줄 번호는 넣지 않는다. 코드가 위아래로 밀려도 같은 엔드포인트로 남아야 하기 때문이다.
/// </summary>
internal static class EndpointIdentity
{
    /// <summary>
    /// 라우트를 비교·ID에 쓸 수 있는 모양으로 맞춘다.
    /// 앞 슬래시를 붙이고, 중복 슬래시를 접고, 끝 슬래시를 떼고, <c>{id:int}</c>·<c>{id?}</c>의 제약·기본값을 없앤다.
    /// </summary>
    public static string NormalizeRoute(string? route)
    {
        var value = (route ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "/";
        }

        var builder = new StringBuilder("/");
        foreach (var segment in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (builder.Length > 1)
            {
                builder.Append('/');
            }

            builder.Append(NormalizeSegment(segment));
        }

        var normalized = builder.ToString();
        return normalized.Length > 1 && normalized.EndsWith('/') ? normalized[..^1] : normalized;
    }

    /// <summary>
    /// 컨트롤러의 클래스 라우트와 메서드 라우트를 잇는다.
    /// MVC 규칙대로 메서드 라우트가 <c>/</c>로 시작하면 절대 경로로 본다.
    /// </summary>
    public static string Combine(string? prefix, string? route) => NormalizeRoute(Join(prefix, route));

    /// <summary>
    /// 잇기만 하고 정규화하지 않는다. 화면에 보여 줄 원문 라우트는 쓴 그대로여야 한다
    /// (<c>v{version:apiVersion}</c>의 제약을 지우면 안 된다).
    /// 메서드 라우트가 <c>/</c>로 시작하면 절대 경로라 접두사를 붙이지 않는다.
    /// </summary>
    public static string Join(string? prefix, string? route)
    {
        var right = (route ?? string.Empty).Trim();
        if (right.StartsWith('/'))
        {
            return right;
        }

        var left = (prefix ?? string.Empty).Trim().Trim('/');
        var parts = new[] { left, right.Trim('/') }.Where(part => part.Length > 0);
        return "/" + string.Join('/', parts);
    }

    /// <summary>
    /// Minimal API의 <c>MapGroup</c> 접두사와 라우트를 잇는다.
    /// 그룹 안의 <c>MapGet("/health")</c>는 절대 경로가 아니라 접두사에 이어 붙는 경로이므로 항상 이어 붙인다.
    /// 여기서는 정규화하지 않는다 — 화면에 보여 줄 라우트는 컨트롤러와 마찬가지로 소스에 쓴 그대로여야 한다
    /// (<c>{customerId}</c>가 <c>{customerid}</c>로 바뀌면 사용자가 소스와 대조할 수 없다).
    /// ID와 비교에 쓰는 값은 호출 측이 <see cref="NormalizeRoute"/>로 따로 만든다.
    /// </summary>
    public static string Append(string? prefix, string? route)
    {
        var left = (prefix ?? string.Empty).Trim().Trim('/');
        var right = (route ?? string.Empty).Trim().Trim('/');
        var parts = new[] { left, right }.Where(part => part.Length > 0);
        return "/" + string.Join('/', parts);
    }

    public static string Create(string projectKey, string httpMethod, string normalizedRoute, string registrationIdentity) =>
        $"{projectKey}|{httpMethod.ToUpperInvariant()}|{normalizedRoute}|{registrationIdentity}";

    /// <summary>
    /// 같은 등록 식별자가 여러 개 나오면(같은 파일에서 같은 핸들러를 같은 라우트로 두 번 등록 등)
    /// 뒤에 순번을 붙여 구분한다. 이 순번은 소스 순서 기반이라 줄 번호보다 안정적이다.
    /// </summary>
    public static string Disambiguate(string endpointId, ISet<string> used)
    {
        if (used.Add(endpointId))
        {
            return endpointId;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{endpointId}#{index}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// <c>{id:int}</c> → <c>{id}</c>, <c>{id?}</c> → <c>{id}</c>. 캐치올(<c>{*rest}</c>)은 별표를 유지한다.
    /// 세그먼트에 리터럴이 섞여 있어도(<c>v{version:apiVersion}</c> → <c>v{version}</c>) 제약만 지운다.
    /// 원문 라우트(<c>entryPoint.route</c>)는 그대로 두므로 화면에는 쓴 대로 보인다.
    /// </summary>
    private static string NormalizeSegment(string segment)
    {
        var builder = new StringBuilder();
        var index = 0;
        while (index < segment.Length)
        {
            var open = segment.IndexOf('{', index);
            if (open < 0)
            {
                builder.Append(segment[index..].ToLowerInvariant());
                break;
            }

            var close = segment.IndexOf('}', open);
            if (close < 0)
            {
                builder.Append(segment[index..].ToLowerInvariant());
                break;
            }

            builder.Append(segment[index..open].ToLowerInvariant());
            builder.Append('{').Append(NormalizeParameter(segment[(open + 1)..close])).Append('}');
            index = close + 1;
        }

        return builder.ToString();
    }

    /// <summary>중괄호 안의 이름만 남긴다. 제약(<c>:int</c>)·기본값(<c>=1</c>)·선택 표시(<c>?</c>)는 뗀다.</summary>
    private static string NormalizeParameter(string inner)
    {
        var colon = inner.IndexOf(':');
        if (colon >= 0)
        {
            inner = inner[..colon];
        }

        var equals = inner.IndexOf('=');
        if (equals >= 0)
        {
            inner = inner[..equals];
        }

        return inner.TrimEnd('?').ToLowerInvariant();
    }
}

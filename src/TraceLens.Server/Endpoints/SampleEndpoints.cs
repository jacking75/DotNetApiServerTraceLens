using TraceLens.Context;

namespace TraceLens.Server.Endpoints;

/// <param name="Available">예제 소스 폴더를 실제로 찾았는지. 소스 빌드에서 samples가 없으면 false다.</param>
public sealed record SampleView(
    string Id,
    string Title,
    string Description,
    string RelativeTargetPath,
    IReadOnlyList<string> Expected,
    IReadOnlyList<string> LearningSteps,
    bool Available);

/// <param name="Destination">복사할 위치. 비우면 <c>%LOCALAPPDATA%\TraceLens\samples\{id}</c>다.</param>
/// <param name="Overwrite">이미 복사돼 있어도 다시 복사할지.</param>
public sealed record PrepareSampleRequest(string? Destination = null, bool Overwrite = false);

/// <param name="Copied">이번 호출에서 실제로 복사했는지. false면 이미 있던 것을 그대로 쓴다.</param>
public sealed record PrepareSampleResponse(string Id, string TargetPath, bool Copied);

/// <summary>
/// 첫 화면의 "예제 분석"이 쓰는 API다.
/// 도구 설치 폴더 안에서 바로 분석하지 않고, 사용자 영역으로 복사한 뒤 그 경로를 분석 대상으로 준다
/// (설치 폴더는 쓰기가 막혀 있을 수 있고, 분석하면 <c>.tracelens</c> 산출물이 생긴다).
/// </summary>
public static class SampleEndpoints
{
    public const string SampleNotFound = "SAMPLE_NOT_FOUND";
    public const string SampleCopyFailed = "SAMPLE_COPY_FAILED";

    public static RouteGroupBuilder MapSampleEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/samples", List);
        group.MapPost("/samples/{id}/prepare", PrepareAsync);
        return group;
    }

    internal static IResult List(IHostEnvironment environment)
    {
        var samples = DemoSamples.Find(environment.ContentRootPath);
        return Results.Ok(samples
            .Select(sample => new SampleView(
                sample.Id,
                sample.Title,
                sample.Description,
                sample.RelativeTargetPath,
                sample.Expected,
                sample.LearningSteps,
                Directory.Exists(sample.SourceDirectory)))
            .ToArray());
    }

    internal static async Task<IResult> PrepareAsync(string id, PrepareSampleRequest? request, IHostEnvironment environment)
    {
        var sample = DemoSamples.Find(environment.ContentRootPath).FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (sample is null)
        {
            return ApiResults.Error(
                StatusCodes.Status404NotFound,
                SampleNotFound,
                "그 예제를 찾을 수 없다. 배포 패키지의 samples 폴더가 있어야 한다.",
                new { id });
        }

        var destination = string.IsNullOrWhiteSpace(request?.Destination)
            ? DemoSamples.DefaultDestination(sample.Id)
            : request.Destination;
        try
        {
            var prepared = await DemoSamples.PrepareAsync(sample, destination, request?.Overwrite ?? false);
            return Results.Ok(new PrepareSampleResponse(sample.Id, prepared.TargetPath, prepared.Copied));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ApiResults.Error(
                StatusCodes.Status500InternalServerError,
                SampleCopyFailed,
                "예제를 복사하지 못했다: " + exception.Message,
                new { destination });
        }
    }
}

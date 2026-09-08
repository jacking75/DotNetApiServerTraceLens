namespace TraceLens.Analyzer.Models;

/// <summary>분석 입력으로 쓴 파일의 종류. 소스(.cs)는 양이 많아 <see cref="GraphDocument.FileHashes"/>에 따로 담는다.</summary>
public enum ManifestInputKind
{
    Solution,
    Project,
    Config,
    BuildProps,
    PackagesProps,
    GlobalJson
}

/// <param name="Path">분석 루트 기준 상대 경로('/' 구분자). 루트 밖이면 전체 경로다.</param>
/// <param name="Hash">파일 내용의 sha256. 읽지 못했으면 <c>unknown</c>이다.</param>
public sealed record ManifestInput(string Path, string Hash, ManifestInputKind Kind);

/// <summary>
/// 이 분석이 무엇을 읽어 만들어졌는지 적어 둔 목록이다.
/// 신선도 확인은 <see cref="GraphDocument.FileHashes"/>의 해시 비교뿐 아니라
/// <see cref="SourceRoots"/> 아래의 <c>.cs</c> 파일 집합 비교로 "새로 생긴 파일"까지 잡는다.
/// </summary>
/// <param name="Inputs">.sln/.csproj와 설정 파일들. 내용이 바뀌면 분석 결과가 달라질 수 있다.</param>
/// <param name="SourceRoots">새 소스 파일을 찾을 때 훑는 디렉터리(분석 루트 기준 상대 경로).</param>
/// <param name="ExcludedDirectories">위 훑기에서 건너뛰는 디렉터리 이름.</param>
/// <param name="LinkedSources">분석 루트 밖에서 링크로 들어온 소스. 해시는 추적하지만 새 파일 검사는 하지 않는다.</param>
/// <param name="Limitations">추적하지 못한 입력(MSBuild import 전개, source generator 산출물 등)을 적는다.</param>
public sealed record AnalysisManifest(
    IReadOnlyList<ManifestInput> Inputs,
    IReadOnlyList<string> SourceRoots,
    IReadOnlyList<string> ExcludedDirectories,
    IReadOnlyList<string> LinkedSources,
    IReadOnlyList<string> Limitations)
{
    public static AnalysisManifest Empty { get; } = new([], [], [], [], []);
}

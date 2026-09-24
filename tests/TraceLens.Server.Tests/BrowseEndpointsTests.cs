using Microsoft.AspNetCore.Http;
using TraceLens.Server.Endpoints;
using Xunit;

namespace TraceLens.Server.Tests;

public sealed class BrowseEndpointsTests
{
    [Fact]
    public void Browse_WithoutPath_ReturnsDriveRoots()
    {
        var response = GetResponse(BrowseEndpoints.Browse(null));

        Assert.Null(response.Path);
        Assert.Null(response.ParentPath);
        Assert.NotEmpty(response.Entries);
        Assert.All(response.Entries, entry => Assert.Equal(BrowseEntryKind.Directory, entry.Kind));
    }

    [Fact]
    public void Browse_ListsTargetFilesAndDirectoriesOnly()
    {
        var directory = CreateFixtureDirectory();
        try
        {
            var response = GetResponse(BrowseEndpoints.Browse(directory));

            Assert.Equal(directory, response.Path);
            Assert.Equal(
                [
                    ("Fixture.Api.csproj", BrowseEntryKind.Project),
                    ("Fixture.sln", BrowseEntryKind.Solution),
                    ("Fixture.slnx", BrowseEntryKind.Solution)
                ],
                response.Entries
                    .Where(entry => entry.Kind != BrowseEntryKind.Directory)
                    .Select(entry => (entry.Name, entry.Kind))
                    .ToArray());
            Assert.Contains(response.Entries, entry => entry is { Name: "src", Kind: BrowseEntryKind.Directory });
            Assert.DoesNotContain(response.Entries, entry => entry.Name is "bin" or "readme.txt");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// .slnx만 있는 폴더가 빈 폴더로 보이면, 잘 써 둔 미지원 사유에 닿을 방법이 없다.
    /// 목록에 보여 주되 고를 수 없게 하고 사유를 함께 준다.
    /// </summary>
    [Fact]
    public void Browse_ListsSlnxAsASolution()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-browse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Fixture.slnx"), string.Empty);
        try
        {
            var response = GetResponse(BrowseEndpoints.Browse(directory));

            var entry = response.Entries.Single(item => item.Name == "Fixture.slnx");
            Assert.Equal(BrowseEntryKind.Solution, entry.Kind);
            Assert.True(string.IsNullOrWhiteSpace(entry.Reason));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Browse_WithFilePath_OpensContainingDirectory()
    {
        var directory = CreateFixtureDirectory();
        try
        {
            var response = GetResponse(BrowseEndpoints.Browse(Path.Combine(directory, "Fixture.sln")));

            Assert.Equal(directory, response.Path);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Browse_WithMissingPath_FallsBackToNearestExistingDirectory()
    {
        var directory = CreateFixtureDirectory();
        try
        {
            var response = GetResponse(BrowseEndpoints.Browse(Path.Combine(directory, "없는폴더", "없는파일.sln")));

            Assert.Equal(directory, response.Path);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateFixtureDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracelens-browse-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        Directory.CreateDirectory(Path.Combine(directory, "bin"));
        File.WriteAllText(Path.Combine(directory, "Fixture.sln"), string.Empty);
        File.WriteAllText(Path.Combine(directory, "Fixture.slnx"), string.Empty);
        File.WriteAllText(Path.Combine(directory, "Fixture.Api.csproj"), string.Empty);
        File.WriteAllText(Path.Combine(directory, "readme.txt"), string.Empty);
        return directory;
    }

    private static BrowseResponse GetResponse(IResult result) =>
        Assert.IsType<BrowseResponse>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
}

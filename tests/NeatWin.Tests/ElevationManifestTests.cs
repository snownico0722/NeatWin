namespace NeatWin.Tests;

public sealed class ElevationManifestTests
{
    [Theory]
    [InlineData("src/NeatWin/app.manifest")]
    [InlineData("src/NeatWin.Recorder/app.manifest")]
    public void ExecutablesRequestAdministratorIntegrity(string relativePath)
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, relativePath));
        Assert.Contains("requestedExecutionLevel level=\"requireAdministrator\"", text, StringComparison.Ordinal);
        Assert.Contains("uiAccess=\"false\"", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/NeatWin/NeatWin.csproj")]
    [InlineData("src/NeatWin.Recorder/NeatWin.Recorder.csproj")]
    public void ProjectsEmbedTheElevationManifest(string relativePath)
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, relativePath));
        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

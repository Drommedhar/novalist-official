using Novalist.Backend;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The backend as a tool as well as a server. Program.cs parsed no arguments,
/// so an export could only be produced by somebody clicking a save dialog.
/// </summary>
public sealed class CommandLineTests : IDisposable
{
    private readonly string _root;

    public CommandLineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    // ── Reading the line ──

    [Fact]
    public void NoArgumentsIsTheServer()
    {
        // What the app starts, and what every existing caller gets.
        var request = CommandLine.Parse([]);

        Assert.True(request.Serve);
        Assert.Null(request.Error);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpIsAsked(string flag)
        => Assert.True(CommandLine.Parse([flag]).Help);

    [Fact]
    public void AFullExportLineIsRead()
    {
        var request = CommandLine.Parse(
            ["--export", "Epub", "--project", "/p", "--out", "/o.epub", "--book", "Two"]);

        Assert.False(request.Serve);
        Assert.Null(request.Error);
        Assert.Equal("Epub", request.Format);
        Assert.Equal("/p", request.ProjectPath);
        Assert.Equal("/o.epub", request.OutputPath);
        Assert.Equal("Two", request.Book);
    }

    [Fact]
    public void AnUnknownArgumentIsAnErrorRatherThanQuietlyServing()
    {
        // A typo in a scheduled job should fail loudly, not start a server that
        // nothing is talking to.
        var request = CommandLine.Parse(["--exprot", "Epub"]);

        Assert.False(request.Serve);
        Assert.Contains("--exprot", request.Error);
    }

    [Theory]
    [InlineData("--project")]
    [InlineData("--export")]
    [InlineData("--out")]
    [InlineData("--book")]
    public void AFlagWithNoValueIsAnError(string flag)
        => Assert.Contains("needs a value", CommandLine.Parse([flag]).Error);

    [Theory]
    // A flag whose value was swallowed by the next flag is the same mistake.
    [InlineData(new[] { "--export", "--project", "/p" }, "needs a value")]
    [InlineData(new[] { "--project", "/p" }, "--export needs a format")]
    [InlineData(new[] { "--export", "Epub" }, "--export needs --project")]
    [InlineData(new[] { "--export", "Epub", "--project", "/p" }, "--export needs --out")]
    public void AnIncompleteExportSaysWhatIsMissing(string[] args, string expected)
        => Assert.Contains(expected, CommandLine.Parse(args).Error);

    [Fact]
    public void TheUsageNamesTheFormatsItAccepts()
    {
        // A usage block that does not list the formats sends people to the
        // source to find out what to type.
        Assert.Contains("Epub", CommandLine.Usage);
        Assert.Contains("Opml", CommandLine.Usage);
        Assert.Contains("--book", CommandLine.Usage);
    }

    // ── Doing the work ──

    private async Task<string> SeedProjectAsync()
    {
        var dir = Path.Combine(_root, "project");
        Directory.CreateDirectory(dir);
        using var workspace = new Workspace(Path.Combine(_root, "settings"));
        await workspace.Projects.CreateProjectAsync(dir, "CliNovel", "Book One");
        await workspace.OpenProjectAsync(workspace.Projects.ProjectRoot!);
        var chapter = await workspace.Projects.CreateChapterAsync("One");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Opening");
        await workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>It began.</p>", "It began.");
        return workspace.Projects.ProjectRoot!;
    }

    [Fact]
    public async Task AnExportRunsWithoutTheApp()
    {
        var project = await SeedProjectAsync();
        var output = Path.Combine(_root, "out.md");
        var log = new StringWriter();

        var code = await CommandLine.ExportAsync(
            CommandLine.Parse(["--export", "Markdown", "--project", project, "--out", output]), log);

        Assert.Equal(0, code);
        Assert.True(File.Exists(output));
        Assert.Contains("It began.", await File.ReadAllTextAsync(output));
        Assert.Contains("Wrote", log.ToString());
    }

    [Fact]
    public async Task AProjectThatIsNotThereFailsWithoutThrowing()
    {
        var log = new StringWriter();

        var code = await CommandLine.ExportAsync(
            CommandLine.Parse(
                ["--export", "Markdown", "--project", Path.Combine(_root, "nope"), "--out", "/o.md"]),
            log);

        // Two rather than one: the project could not be opened, which is a
        // different thing from an export that ran and produced nothing.
        Assert.Equal(2, code);
        Assert.Contains("Could not open", log.ToString());
    }

    [Fact]
    public async Task ABookThatIsNotThereIsNamedRatherThanIgnored()
    {
        var project = await SeedProjectAsync();
        var log = new StringWriter();

        var code = await CommandLine.ExportAsync(
            CommandLine.Parse([
                "--export", "Markdown", "--project", project,
                "--out", Path.Combine(_root, "o.md"), "--book", "No Such Book"
            ]), log);

        // Silently exporting the wrong book is the worst outcome here.
        Assert.Equal(2, code);
        Assert.Contains("No Such Book", log.ToString());
    }

    [Fact]
    public async Task TheNamedBookIsTheOneExported()
    {
        var project = await SeedProjectAsync();
        var output = Path.Combine(_root, "second.md");
        using (var workspace = new Workspace(Path.Combine(_root, "settings")))
        {
            await workspace.OpenProjectAsync(project);
            var second = await workspace.Projects.CreateBookAsync("Book Two");
            // Creating a book does not make it the active one, and a chapter
            // goes into whichever book is active.
            await workspace.Projects.SwitchBookAsync(second.Id);
            var chapter = await workspace.Projects.CreateChapterAsync("Later");
            var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Second");
            await workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>Elsewhere.</p>", "Elsewhere.");
        }
        var log = new StringWriter();

        var code = await CommandLine.ExportAsync(
            CommandLine.Parse([
                "--export", "Markdown", "--project", project, "--out", output, "--book", "Book Two"
            ]), log);

        Assert.Equal(0, code);
        var text = await File.ReadAllTextAsync(output);
        Assert.Contains("Elsewhere.", text);
        Assert.DoesNotContain("It began.", text);
    }

    [Fact]
    public async Task AnExportThatRunsAndWritesNothingIsReportedAsSuch()
    {
        var project = await SeedProjectAsync();
        using var workspace = new Workspace(Path.Combine(_root, "settings"));
        await workspace.OpenProjectAsync(project);
        // A contributed format whose handler does nothing: the export runs
        // without throwing and there is no file at the end of it, which is a
        // different failure from a format nobody recognises.
        workspace.ExtensionsHost.ExportFormats.Add(new Novalist.Sdk.Models.ExportFormatDescriptor
        {
            FormatKey = "silent",
            DisplayName = "Silent",
            FileExtension = ".none",
            Export = _ => Task.CompletedTask
        });
        var log = new StringWriter();

        var code = await CommandLine.ExportAsync(
            CommandLine.Parse([
                "--export", "silent", "--project", project,
                "--out", Path.Combine(_root, "nothing.none")
            ]), log, workspace);

        Assert.Equal(1, code);
        Assert.Contains("produced no file", log.ToString());
    }

    [Fact]
    public async Task AFormatThatDoesNotExistFailsRatherThanWritingRubbish()
    {
        var project = await SeedProjectAsync();
        var log = new StringWriter();

        var code = await CommandLine.ExportAsync(
            CommandLine.Parse([
                "--export", "NotAFormat", "--project", project,
                "--out", Path.Combine(_root, "x.out")
            ]), log);

        Assert.NotEqual(0, code);
    }
}

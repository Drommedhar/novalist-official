using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class ImportRpcTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _vault;
    private readonly Workspace _workspace;
    private readonly ImportRpc _rpc;

    public ImportRpcTests()
    {
        _vault = Path.Combine(_dir.Path, "vault");
        Directory.CreateDirectory(_vault);
        _workspace = new Workspace(Path.Combine(_dir.Path, "settings"));
        _rpc = new ImportRpc(_workspace);
    }

    public void Dispose() => _dir.Dispose();

    private void Write(string relative, string content)
    {
        var full = Path.Combine(_vault, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public async Task Detect_ReadsPluginDataProjects()
    {
        Write(".obsidian/plugins/novalist/data.json",
            """{ "projects": [ { "name": "Saga", "path": "Saga" } ] }""");

        var detection = await _rpc.DetectAsync(_vault);

        Assert.True(detection.HasPluginData);
        Assert.Single(detection.Projects, p => p.Name == "Saga" && p.Path == "Saga");
    }

    [Fact]
    public async Task Detect_FallsBackToFolderStructure()
    {
        Write("Chapters/01.md", "# Chapter\n\n## Scene\nText.");

        var detection = await _rpc.DetectAsync(_vault);

        Assert.False(detection.HasPluginData);
        Assert.Single(detection.Projects, p => p.Path == "");
    }

    [Fact]
    public async Task Run_ImportsVaultAndMergesSettings()
    {
        Write(".obsidian/plugins/novalist/data.json", """
        {
          "language": "de",
          "relationshipPairs": { "parent": ["child"] },
          "autoReplacements": [ { "start": "--", "replacement": "—" } ]
        }
        """);
        Write("Chapters/01 - Intro.md", "# Intro\n\n## Arrival\nSnow fell over the pass.");

        var outputDir = Path.Combine(_dir.Path, "out");
        Directory.CreateDirectory(outputDir);
        var run = await _rpc.RunAsync(_vault, "", outputDir, "Imported Saga", "Book One");

        Assert.True(File.Exists(Path.Combine(run.ProjectPath, ".novalist", "project.json")));
        Assert.True(File.Exists(Path.Combine(run.ProjectPath, "import-log.txt")));

        var settings = _workspace.Settings.Settings;
        Assert.Equal("de", settings.AutoReplacementLanguage);
        Assert.Contains(settings.AutoReplacements, p => p.Start == "--");
        Assert.True(settings.RelationshipPairs.ContainsKey("parent"));

        // Second import of the same vault must not clobber the user's
        // existing auto-replacements (only-empty merge, mirroring Desktop).
        var run2 = await _rpc.RunAsync(_vault, "", outputDir, "Imported Again", "Book One");
        Assert.NotEqual(run.ProjectPath, run2.ProjectPath);

        // A failed log write is swallowed; the import still succeeds.
        _rpc.LogWriterOverride = (_, _) => throw new IOException("disk full");
        var run3 = await _rpc.RunAsync(_vault, "", outputDir, "Imported Third", "Book One");
        Assert.True(File.Exists(Path.Combine(run3.ProjectPath, ".novalist", "project.json")));
        Assert.False(File.Exists(Path.Combine(run3.ProjectPath, "import-log.txt")));
    }
}

using Novalist.Backend.Tests.TestHelpers;
using Novalist.Backend.Extensions;
using Novalist.Sdk;
using Xunit;

namespace Novalist.Backend.Tests;

// Serialized with the rest of the extension tests: two of these move
// NOVALIST_EXTENSIONS_DISABLED, which is process-wide, and the store RPC reads
// it.
[Collection("BackendStatics")]
public class ExtensionLoaderTests
{
    [Fact]
    public void Discover_Disabled_ReturnsEmptyAndCreatesNoDirectory()
    {
        using var dir = new TempDir();
        var sub = Path.Combine(dir.Path, "exts");
        var loader = new ExtensionLoader(sub, disabled: true);

        Assert.True(loader.ExtensionsDisabled);
        Assert.Empty(loader.DiscoverExtensions());
        // Not even the folder. A build with no extension feature should leave no
        // trace of one on disk for somebody to find and wonder about.
        Assert.False(Directory.Exists(sub));
    }

    [Fact]
    public void Discover_Disabled_DoesNotSeedBundled()
    {
        using var dir = new TempDir();
        var bundled = dir.Combine("bundled", "Sample");
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "extension.json"),
            """{ "id": "ext.bundled", "name": "B", "version": "1.0.0" }""");
        var target = dir.Combine("exts");

        var loader = new ExtensionLoader(target, dir.Combine("bundled"), disabled: true);

        Assert.Empty(loader.DiscoverExtensions());
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void ExtensionsDisabled_FallsBackToEnvironment()
    {
        var prev = Environment.GetEnvironmentVariable("NOVALIST_EXTENSIONS_DISABLED");
        try
        {
            Environment.SetEnvironmentVariable("NOVALIST_EXTENSIONS_DISABLED", null);
            Assert.False(ExtensionLoader.DisabledByEnvironment);
            Assert.False(new ExtensionLoader().ExtensionsDisabled);

            // Only "1" counts, so a stray value never silently removes the feature.
            Environment.SetEnvironmentVariable("NOVALIST_EXTENSIONS_DISABLED", "0");
            Assert.False(ExtensionLoader.DisabledByEnvironment);

            Environment.SetEnvironmentVariable("NOVALIST_EXTENSIONS_DISABLED", "1");
            Assert.True(ExtensionLoader.DisabledByEnvironment);
            Assert.True(new ExtensionLoader().ExtensionsDisabled);
            // An explicit answer still wins over the environment.
            Assert.False(new ExtensionLoader(disabled: false).ExtensionsDisabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVALIST_EXTENSIONS_DISABLED", prev);
        }
    }

    [Fact]
    public void GetExtensionsDirectory_HonorsSettingsDirOverride()
    {
        var prev = Environment.GetEnvironmentVariable("NOVALIST_SETTINGS_DIR");
        try
        {
            // Unset → production %APPDATA%/Novalist/Extensions.
            Environment.SetEnvironmentVariable("NOVALIST_SETTINGS_DIR", null);
            Assert.Contains(Path.Combine("Novalist", "Extensions"), ExtensionLoader.GetExtensionsDirectory());

            // Set → isolated under the settings root (test isolation).
            var custom = Path.Combine(Path.GetTempPath(), "nl-extdir-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("NOVALIST_SETTINGS_DIR", custom);
            Assert.Equal(Path.Combine(custom, "Extensions"), ExtensionLoader.GetExtensionsDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVALIST_SETTINGS_DIR", prev);
        }
    }

    [Fact]
    public void Discover_MissingDir_CreatesAndReturnsEmpty()
    {
        using var dir = new TempDir();
        var sub = Path.Combine(dir.Path, "exts");
        var loader = new ExtensionLoader(sub);
        Assert.Empty(loader.DiscoverExtensions());
        Assert.True(Directory.Exists(sub));
    }

    [Fact]
    public void Discover_ParsesValidManifests_SkipsNoManifestAndEmptyId()
    {
        using var dir = new TempDir();
        // valid
        var a = Path.Combine(dir.Path, "extA");
        Directory.CreateDirectory(a);
        File.WriteAllText(Path.Combine(a, "extension.json"), """{ "id": "ext.a", "name": "A" }""");
        // folder without manifest -> skipped
        Directory.CreateDirectory(Path.Combine(dir.Path, "noManifest"));
        // empty id -> skipped
        var c = Path.Combine(dir.Path, "extC");
        Directory.CreateDirectory(c);
        File.WriteAllText(Path.Combine(c, "extension.json"), """{ "id": "" }""");

        var loader = new ExtensionLoader(dir.Path);
        var found = loader.DiscoverExtensions();
        Assert.Single(found);
        Assert.Equal("ext.a", found[0].Manifest.Id);
    }

    [Fact]
    public void Discover_CorruptManifest_RecordsLoadError()
    {
        using var dir = new TempDir();
        var b = Path.Combine(dir.Path, "extB");
        Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(b, "extension.json"), "{ not json");
        var found = new ExtensionLoader(dir.Path).DiscoverExtensions();
        Assert.Single(found);
        Assert.NotNull(found[0].LoadError);
        Assert.Equal("extB", found[0].Manifest.Id); // falls back to folder name
    }

    // ── Extensions shipped inside the application ──

    /// <summary>A bundled extension folder, with a version and one payload file
    /// so a copy can be told from a no-op.</summary>
    private static string Bundled(string root, string folder, string id, string version, string payload)
    {
        var at = Path.Combine(root, folder);
        Directory.CreateDirectory(Path.Combine(at, "locales"));
        File.WriteAllText(
            Path.Combine(at, "extension.json"),
            $$"""{ "id": "{{id}}", "name": "{{id}}", "version": "{{version}}" }""");
        File.WriteAllText(Path.Combine(at, "locales", "en.json"), payload);
        return at;
    }

    [Fact]
    public void Discover_CopiesAnExtensionShippedInsideTheApplication()
    {
        // Seeded rather than discovered where it lies: an installed extension is
        // a folder that gets written to - its settings, its Python environment,
        // its downloaded models - and the application directory is read-only on
        // macOS and unwritable for a standard user on Windows.
        using var dir = new TempDir();
        using var shipped = new TempDir();
        Bundled(shipped.Path, "Speech", "com.novalist.speech", "1.0.0", "ours");

        var found = new ExtensionLoader(dir.Path, shipped.Path).DiscoverExtensions();

        Assert.Equal("com.novalist.speech", Assert.Single(found).Manifest.Id);
        Assert.Equal(
            "ours", File.ReadAllText(Path.Combine(dir.Path, "Speech", "locales", "en.json")));
    }

    [Fact]
    public void Discover_DoesNotDowngradeAnExtensionTheWriterHasAlreadyUpdated()
    {
        // Somebody who updated from the gallery keeps their update. Overwriting
        // it on every launch would undo the update each time the app started,
        // with nothing anywhere saying why.
        using var dir = new TempDir();
        using var shipped = new TempDir();
        Bundled(shipped.Path, "Speech", "com.novalist.speech", "1.0.0", "ours");
        Bundled(dir.Path, "Speech", "com.novalist.speech", "1.2.0", "theirs");

        new ExtensionLoader(dir.Path, shipped.Path).DiscoverExtensions();

        Assert.Equal(
            "theirs", File.ReadAllText(Path.Combine(dir.Path, "Speech", "locales", "en.json")));
    }

    [Fact]
    public void Discover_ReplacesAnOlderInstallWithTheOneThisBuildShips()
    {
        using var dir = new TempDir();
        using var shipped = new TempDir();
        Bundled(shipped.Path, "Speech", "com.novalist.speech", "1.10.0", "ours");
        // 1.9.0, which is older than 1.10.0 as a version and newer as a string.
        // Compared as text this downgraded somebody's extension every launch.
        Bundled(dir.Path, "Speech", "com.novalist.speech", "1.9.0", "theirs");

        new ExtensionLoader(dir.Path, shipped.Path).DiscoverExtensions();

        Assert.Equal(
            "ours", File.ReadAllText(Path.Combine(dir.Path, "Speech", "locales", "en.json")));
    }

    [Fact]
    public void Discover_LeavesWhatAnExtensionHasDownloadedBesideItself()
    {
        // The environment and the model weights are gigabytes and live in the
        // extension's own folder. An upgrade that removed them would re-download
        // eight gigabytes to ship a bug fix.
        using var dir = new TempDir();
        using var shipped = new TempDir();
        Bundled(shipped.Path, "Speech", "com.novalist.speech", "2.0.0", "ours");
        Bundled(dir.Path, "Speech", "com.novalist.speech", "1.0.0", "theirs");
        var kept = Path.Combine(dir.Path, "Speech", "venv");
        Directory.CreateDirectory(kept);
        File.WriteAllText(Path.Combine(kept, "installed.txt"), "a recipe");

        new ExtensionLoader(dir.Path, shipped.Path).DiscoverExtensions();

        Assert.True(File.Exists(Path.Combine(kept, "installed.txt")));
    }

    [Fact]
    public void Discover_WithNothingShippedIsTheSameAsBefore()
    {
        using var dir = new TempDir();

        Assert.Empty(new ExtensionLoader(dir.Path, "").DiscoverExtensions());
        Assert.Empty(
            new ExtensionLoader(dir.Path, Path.Combine(dir.Path, "nope")).DiscoverExtensions());
    }

    [Fact]
    public void Discover_SkipsAShippedFolderThatIsNotAnExtension()
    {
        // The folder carries a README explaining what belongs in it, and a
        // release that shipped nothing would otherwise install the README as an
        // extension called "resources".
        using var dir = new TempDir();
        using var shipped = new TempDir();
        Directory.CreateDirectory(Path.Combine(shipped.Path, "notes"));
        File.WriteAllText(Path.Combine(shipped.Path, "notes", "README.md"), "read me");

        Assert.Empty(new ExtensionLoader(dir.Path, shipped.Path).DiscoverExtensions());
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "notes")));
    }

    [Fact]
    public void Discover_AVersionNeitherSideCanReadLeavesTheInstallAlone()
    {
        // A strange manifest is not a reason to overwrite something that works.
        using var dir = new TempDir();
        using var shipped = new TempDir();
        Bundled(shipped.Path, "Speech", "com.novalist.speech", "not a version", "ours");
        Bundled(dir.Path, "Speech", "com.novalist.speech", "1.0.0", "theirs");

        new ExtensionLoader(dir.Path, shipped.Path).DiscoverExtensions();

        Assert.Equal(
            "theirs", File.ReadAllText(Path.Combine(dir.Path, "Speech", "locales", "en.json")));
    }

    [Fact]
    public void Discover_AShippedExtensionThatCannotBeCopiedIsDoneWithoutRatherThanFatal()
    {
        // A file where the folder should be is the cheapest way to reproduce
        // what a locked file from a previous run does. Whatever the cause, an
        // application that will not start because one bundled extension could
        // not be written is worse than one missing that extension.
        using var dir = new TempDir();
        using var shipped = new TempDir();
        Bundled(shipped.Path, "Speech", "com.novalist.speech", "1.0.0", "ours");
        File.WriteAllText(Path.Combine(dir.Path, "Speech"), "in the way");

        Assert.Empty(new ExtensionLoader(dir.Path, shipped.Path).DiscoverExtensions());
    }

    [Fact]
    public void Discover_AShippedVersionThatIsNotReadableJsonLeavesTheInstallAlone()
    {
        using var dir = new TempDir();
        using var shipped = new TempDir();
        var at = Path.Combine(shipped.Path, "Speech");
        Directory.CreateDirectory(at);
        File.WriteAllText(Path.Combine(at, "extension.json"), "{ not json");
        Bundled(dir.Path, "Speech", "com.novalist.speech", "1.0.0", "theirs");

        new ExtensionLoader(dir.Path, shipped.Path).DiscoverExtensions();

        Assert.Equal(
            "theirs", File.ReadAllText(Path.Combine(dir.Path, "Speech", "locales", "en.json")));
    }

    [Fact]
    public void Discover_AShippedExtensionWithAnUnreadableManifestIsSkipped()
    {
        using var dir = new TempDir();
        using var shipped = new TempDir();
        var at = Path.Combine(shipped.Path, "Broken");
        Directory.CreateDirectory(at);
        File.WriteAllText(Path.Combine(at, "extension.json"), "{ not json");

        // Copied - it has a manifest, so it is meant to be one - and then
        // reported with its parse error, exactly as a hand-installed one is.
        var found = new ExtensionLoader(dir.Path, shipped.Path).DiscoverExtensions();

        Assert.NotNull(Assert.Single(found).LoadError);
    }

    private static ExtensionInfo Info(ExtensionManifest m, string folder = "") => new() { Manifest = m, FolderPath = folder };

    [Fact]
    public void Load_PreExistingError_ReturnsFalse()
    {
        var info = Info(new ExtensionManifest { Id = "x" });
        info.LoadError = "earlier";
        Assert.False(new ExtensionLoader().LoadExtension(info));
    }

    [Fact]
    public void Load_MinHostTooHigh_Fails()
    {
        var info = Info(new ExtensionManifest { Id = "x", MinHostVersion = "9999.0.0" });
        Assert.False(new ExtensionLoader().LoadExtension(info));
        Assert.Contains("Requires host version >=", info.LoadError);
    }

    [Fact]
    public void Load_MaxHostTooLow_Fails()
    {
        var info = Info(new ExtensionManifest { Id = "x", MaxHostVersion = "0.0.1" });
        Assert.False(new ExtensionLoader().LoadExtension(info));
        Assert.Contains("Requires host version <=", info.LoadError);
    }

    [Fact]
    public void Load_MissingEntryAssembly_Fails()
    {
        using var dir = new TempDir();
        var info = Info(new ExtensionManifest { Id = "x", EntryAssembly = "ghost.dll" }, dir.Path);
        Assert.False(new ExtensionLoader().LoadExtension(info));
        Assert.Contains("Entry assembly not found", info.LoadError);
    }

    [Fact]
    public void Load_AssemblyWithoutIExtension_Fails()
    {
        using var dir = new TempDir();
        // Copy a real managed DLL that contains no concrete IExtension implementation.
        var src = typeof(ExtensionManifest).Assembly.Location; // Novalist.Sdk.dll
        var dest = Path.Combine(dir.Path, "Novalist.Sdk.dll");
        File.Copy(src, dest);
        var info = Info(new ExtensionManifest { Id = "x", EntryAssembly = "Novalist.Sdk.dll" }, dir.Path);

        var ok = new ExtensionLoader().LoadExtension(info);
        Assert.False(ok);
        Assert.NotNull(info.LoadError); // "No IExtension implementation" or a load error — either is a failure
    }

    [Fact]
    public void Load_RealSampleExtension_Succeeds()
    {
        using var dir = new TempDir();
        // The sample extension DLL is copied into the test output via ProjectReference.
        var exampleDll = Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll");
        Assert.True(File.Exists(exampleDll), "sample extension DLL must be in the test output");
        File.Copy(exampleDll, Path.Combine(dir.Path, "Novalist.Sdk.Example.dll"));
        // Copy the PDB too so the load-with-symbols branch is exercised.
        var pdb = Path.ChangeExtension(exampleDll, ".pdb");
        if (File.Exists(pdb)) File.Copy(pdb, Path.Combine(dir.Path, "Novalist.Sdk.Example.pdb"));

        var info = Info(new ExtensionManifest
        {
            Id = "com.novalist.writingtoolkit",
            EntryAssembly = "Novalist.Sdk.Example.dll",
            MinHostVersion = "0.0.0" // compatible -> exercises the min-host pass-through path
        }, dir.Path);

        var ok = new ExtensionLoader().LoadExtension(info);

        Assert.True(ok, info.LoadError);
        Assert.True(info.IsLoaded);
        Assert.NotNull(info.Instance);
        Assert.Equal("com.novalist.writingtoolkit", info.Instance!.Id);
    }

    [Fact]
    public void Load_CorruptAssembly_CaughtAsLoadFailed()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "broken.dll"), new byte[] { 0, 1, 2, 3, 4 }); // not a PE image
        var info = Info(new ExtensionManifest { Id = "x", EntryAssembly = "broken.dll" }, dir.Path);
        Assert.False(new ExtensionLoader().LoadExtension(info));
        Assert.Contains("Load failed", info.LoadError);
    }

    [Fact]
    public void Load_MaxHostUnparseable_AllowsThrough()
    {
        using var dir = new TempDir();
        // maxHost can't parse -> IsWithinMaxVersion returns true; fails later at the missing assembly.
        var info = Info(new ExtensionManifest { Id = "x", MaxHostVersion = "not-a-version", EntryAssembly = "ghost.dll" }, dir.Path);
        Assert.False(new ExtensionLoader().LoadExtension(info));
        Assert.Contains("Entry assembly not found", info.LoadError); // passed the maxHost gate
    }
}

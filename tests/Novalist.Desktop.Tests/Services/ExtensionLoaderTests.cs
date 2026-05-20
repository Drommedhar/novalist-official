using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Services;
using Novalist.Sdk;
using Xunit;

namespace Novalist.Desktop.Tests.Services;

public class ExtensionLoaderTests
{
    [Fact]
    public void GetExtensionsDirectory_UnderAppData()
        => Assert.Contains("Extensions", ExtensionLoader.GetExtensionsDirectory());

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

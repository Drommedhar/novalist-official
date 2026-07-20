using Novalist.Backend;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class DataMigrationTests : IDisposable
{
    private readonly string _root;

    public DataMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Legacy()
    {
        var legacy = Path.Combine(_root, "legacy");
        Directory.CreateDirectory(Path.Combine(legacy, "Extensions", "AiAssistant"));
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, "Extensions", "AiAssistant", "extension.json"), "{}");
        return legacy;
    }

    [Fact]
    public void FreshTarget_CopiesLegacyContents()
    {
        var legacy = Legacy();
        var target = Path.Combine(_root, "target");

        DataMigration.MigrateLegacyIfNeeded(target, legacy);

        Assert.True(File.Exists(Path.Combine(target, "settings.json")));
        Assert.True(File.Exists(Path.Combine(target, "Extensions", "AiAssistant", "extension.json")));
    }

    [Fact]
    public void NullOrEmptyTarget_NoOp()
    {
        DataMigration.MigrateLegacyIfNeeded(null, Legacy());
        DataMigration.MigrateLegacyIfNeeded("  ", Legacy());
        // Nothing to assert beyond not throwing.
    }

    [Fact]
    public void SameLocation_NoOp()
    {
        var legacy = Legacy();
        DataMigration.MigrateLegacyIfNeeded(legacy, legacy);
        // Still exactly one settings.json (not duplicated / cleared).
        Assert.True(File.Exists(Path.Combine(legacy, "settings.json")));
    }

    [Fact]
    public void MissingLegacy_NoOp()
    {
        var target = Path.Combine(_root, "target");
        DataMigration.MigrateLegacyIfNeeded(target, Path.Combine(_root, "does-not-exist"));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void TargetHasSettings_NotClobbered()
    {
        var legacy = Legacy();
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "settings.json"), "{\"kept\":true}");

        DataMigration.MigrateLegacyIfNeeded(target, legacy);

        Assert.Contains("kept", File.ReadAllText(Path.Combine(target, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(target, "Extensions")));
    }

    [Fact]
    public void TargetHasExtensions_NotClobbered()
    {
        var legacy = Legacy();
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(target, "Extensions", "Other"));

        DataMigration.MigrateLegacyIfNeeded(target, legacy);

        Assert.False(File.Exists(Path.Combine(target, "settings.json")));
        Assert.True(Directory.Exists(Path.Combine(target, "Extensions", "Other")));
        Assert.False(Directory.Exists(Path.Combine(target, "Extensions", "AiAssistant")));
    }

    [Fact]
    public void DefaultLegacyRoot_ResolvesWithoutThrowing()
    {
        // Exercises the null-legacyRoot branch (falls back to ApplicationData/Novalist).
        // The target already has settings, so it is a guaranteed no-op regardless
        // of whether the machine has a legacy dir.
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "settings.json"), "{}");
        DataMigration.MigrateLegacyIfNeeded(target);
        Assert.True(File.Exists(Path.Combine(target, "settings.json")));
    }
}

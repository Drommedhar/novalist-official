using NSubstitute;
using Novalist.Backend.Extensions;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Services;
using Novalist.Sdk;
using Xunit;

namespace Novalist.Backend.Tests;

// The override also affects gallery and extension tests in other collections.
[CollectionDefinition("VersionEnvironment", DisableParallelization = true)]
public sealed class VersionEnvironmentCollection;

[Collection("VersionEnvironment")]
public class ForcedVersionTests
{
    [Theory]
    [InlineData("3.3.0", "3.3.0", "3.3.0", null)]
    [InlineData("3.2.0", "3.3.0", "", "Requires host version >= 3.3.0 (current: 3.2.0)")]
    [InlineData("3.4.0", "", "3.3.0", "Requires host version <= 3.3.0 (current: 3.4.0)")]
    public void Extensions_UseForcedVersion_ForHostServicesAndCompatibility(
        string version, string minimum, string maximum, string? compatibilityError)
    {
        var previous = Environment.GetEnvironmentVariable("NOVALIST_FORCE_VERSION");
        try
        {
            Environment.SetEnvironmentVariable("NOVALIST_FORCE_VERSION", $"  {version}  ");
            using var host = new HostServices(
                Substitute.For<IFileService>(), Substitute.For<IProjectService>(),
                Substitute.For<IEntityService>(), Substitute.For<ISettingsService>());
            Assert.Equal(version, host.HostVersion);

            using var dir = new TempDir();
            var info = new ExtensionInfo
            {
                FolderPath = dir.Path,
                Manifest = new ExtensionManifest
                {
                    Id = "forced-version", EntryAssembly = "missing.dll",
                    MinHostVersion = minimum, MaxHostVersion = maximum
                }
            };
            Assert.False(new ExtensionLoader(disabled: false).LoadExtension(info));
            // A compatible manifest must get past both version gates to assembly loading.
            Assert.Equal(compatibilityError ?? "Entry assembly not found: missing.dll", info.LoadError);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVALIST_FORCE_VERSION", previous);
        }
    }
}

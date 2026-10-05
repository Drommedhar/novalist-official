using System.IO.Compression;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class ExtensionGalleryInstallationPermissionsTests
{
    [Fact]
    public async Task Install_OverwritesWritableFilesWhenUnixDirectoryCannotBeRemoved()
    {
        if (OperatingSystem.IsWindows()) return;

        using var directory = new TempDir();
        var extensions = Path.Combine(directory.Path, "Extensions");
        var target = Path.Combine(extensions, "example");
        Directory.CreateDirectory(target);
        var manifestPath = Path.Combine(target, "extension.json");
        await File.WriteAllTextAsync(manifestPath, "{\"id\":\"example\",\"version\":\"1.0.0\"}");
        await File.WriteAllTextAsync(Path.Combine(target, "store-meta.json"), "{}");

        var package = Path.Combine(directory.Path, "upgrade.zip");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("extension.json").Open());
            await writer.WriteAsync("{\"id\":\"example\",\"version\":\"2.0.0\"}");
        }

        var originalMode = File.GetUnixFileMode(target);
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            using var client = new HttpClient();
            var gallery = new ExtensionGalleryService(client, extensions, directory.Path);
            await gallery.InstallExtensionAsync(package,
                new GalleryEntry { Id = "example", Repo = "example/repository" },
                new GalleryRelease { Version = "2.0.0" });

            Assert.Equal("2.0.0", gallery.ReadInstalledManifestVersion("example"));
            Assert.Equal("2.0.0", gallery.ReadStoreMeta("example")!.InstalledVersion);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserExecute, File.GetUnixFileMode(target));
            Assert.False(File.Exists(package));
        }
        finally
        {
            File.SetUnixFileMode(target, originalMode);
        }
    }
}

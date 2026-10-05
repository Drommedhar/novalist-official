using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Novalist.Core.Services;

/// <summary>
/// Assigns backup directories to project locations. Existing unmarked archives
/// have no reliable owner and are left available for explicit recovery only.
/// </summary>
internal sealed class BackupStorage(IFileService files)
{
    internal const string OwnerFileName = ".novalist-backup-owner";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<string> ResolveAsync(string projectRoot, string preferredFolder)
    {
        if (IsWithinProject(projectRoot, preferredFolder))
            throw new InvalidOperationException("Choose a backup folder outside the project folder.");

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        var owner = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner))).ToLowerInvariant();

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var candidates = new List<string> { preferredFolder };
            candidates.AddRange((await files.GetDirectoriesAsync(preferredFolder).ConfigureAwait(false))
                .Where(path => files.GetFileName(path) == name ||
                    files.GetFileName(path).StartsWith(name + "-", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal));

            // Search existing ownership first: removing old unowned ZIPs must
            // never switch a project away from its established history.
            foreach (var candidate in candidates)
                if (await IsOwnedAsync(candidate, owner).ConfigureAwait(false))
                    return candidate;

            if (await TryClaimAsync(preferredFolder, owner).ConfigureAwait(false))
                return preferredFolder;

            for (var suffix = 1; ; suffix++)
            {
                var folderName = suffix == 1 ? name : name + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                var candidate = files.CombinePath(preferredFolder, folderName);
                if (await TryClaimAsync(candidate, owner).ConfigureAwait(false))
                    return candidate;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<bool> IsOwnedAsync(string folder, string owner)
    {
        var marker = files.CombinePath(folder, OwnerFileName);
        return await files.ExistsAsync(marker).ConfigureAwait(false) &&
            string.Equals(await files.ReadTextAsync(marker).ConfigureAwait(false), owner, StringComparison.Ordinal);
    }

    private async Task<bool> TryClaimAsync(string folder, string owner)
    {
        if (await IsOwnedAsync(folder, owner).ConfigureAwait(false))
            return true;
        // Even an empty preexisting directory has no proven owner. Reserve a
        // new directory atomically rather than inspecting and then claiming an
        // existing one while an older app could still be writing archives.
        if (await files.DirectoryExistsAsync(folder).ConfigureAwait(false))
            return false;

        var temporary = files.CombinePath(files.GetDirectoryName(folder), ".novalist-backup-claim-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Publish a complete directory and marker in one no-overwrite
            // rename. A competing process either sees our complete ownership
            // record or wins the reservation itself.
            await files.CreateDirectoryAsync(temporary).ConfigureAwait(false);
            await files.WriteTextAsync(files.CombinePath(temporary, OwnerFileName), owner).ConfigureAwait(false);
            try
            {
                await files.MoveDirectoryAsync(temporary, folder).ConfigureAwait(false);
            }
            catch (IOException)
            {
                if (!await files.DirectoryExistsAsync(folder).ConfigureAwait(false))
                    throw;
                return await IsOwnedAsync(folder, owner).ConfigureAwait(false);
            }
        }
        finally
        {
            await files.DeleteDirectoryAsync(temporary).ConfigureAwait(false);
        }
        return true;
    }

    internal static bool IsWithinProject(string projectRoot, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(projectRoot), Path.GetFullPath(candidate));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}

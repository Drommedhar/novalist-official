using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Novalist.Desktop.Services;

/// <summary>
/// A collectible <see cref="AssemblyLoadContext"/> for loading a single extension.
/// Being collectible allows the context (and all its loaded assemblies) to be
/// unloaded so they can be updated in place.
/// All assemblies are loaded from streams to avoid holding file locks.
/// </summary>
internal sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>
    /// Assembly name prefixes that must be resolved from the default (host) context.
    /// This prevents the extension from loading its own copy of shared types,
    /// which would break <c>typeof(IExtension).IsAssignableFrom</c> checks.
    /// </summary>
    private static readonly string[] HostPrefixes =
    [
        "Novalist.Sdk",
        "Novalist.Core",
        "Avalonia",
        "CommunityToolkit",
        "System",
        "Microsoft",
    ];

    public ExtensionLoadContext(string entryAssemblyPath)
        : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;

        // Never load host assemblies into this context — they must come from the default context
        foreach (var prefix in HostPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        // Try the extension's own dependencies (via its .deps.json)
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path != null)
        {
            return LoadFromFileStream(path);
        }

        // Fall back to the default context
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }

    /// <summary>
    /// Loads an assembly by reading the file into memory first, so no file lock is held.
    /// </summary>
    internal Assembly LoadFromFileStream(string assemblyPath)
    {
        using var dll = File.OpenRead(assemblyPath);

        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (File.Exists(pdbPath))
        {
            using var pdb = File.OpenRead(pdbPath);
            return LoadFromStream(dll, pdb);
        }

        return LoadFromStream(dll);
    }
}

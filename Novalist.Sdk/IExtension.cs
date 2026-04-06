using Novalist.Sdk.Services;

namespace Novalist.Sdk;

/// <summary>
/// Main entry point for a Novalist extension.
/// One class per assembly must implement this interface.
/// </summary>
public interface IExtension
{
    /// <summary>Unique extension identifier (reverse-domain, e.g. "com.example.writingtoolkit").</summary>
    string Id { get; }

    /// <summary>Human-readable display name.</summary>
    string DisplayName { get; }

    /// <summary>Short description of what this extension does.</summary>
    string Description { get; }

    /// <summary>Semantic version string.</summary>
    string Version { get; }

    /// <summary>Author name.</summary>
    string Author { get; }

    /// <summary>
    /// Called once when the extension is loaded. Use to register hooks and initialize state.
    /// </summary>
    void Initialize(IHostServices host);

    /// <summary>
    /// Called when the extension is being unloaded or disabled.
    /// </summary>
    void Shutdown();
}

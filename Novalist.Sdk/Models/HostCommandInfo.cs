namespace Novalist.Sdk.Models;

/// <summary>
/// A command something can be asked to run, named by a stable id.
///
/// The point of describing commands rather than exposing methods is that a
/// script does not link against anything: it has a string, and it needs to be
/// able to find out what strings exist and what they take before it uses one.
/// </summary>
public sealed class HostCommandInfo
{
    /// <summary>
    /// Stable id, namespaced by whoever owns it - <c>novalist.scene.create</c>,
    /// <c>com.example.tool.run</c>. This is what a script types, so it changing
    /// breaks that script.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>What to call it in a command list.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>What running it does, in a sentence.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// A JSON Schema for the arguments object, or empty for a command that takes
    /// none. Not enforced by the host - it is documentation a script can read
    /// rather than a contract the host checks.
    /// </summary>
    public string ArgumentsSchema { get; init; } = string.Empty;

    /// <summary>
    /// Whether running it changes the project. A script runner can offer to
    /// confirm these and let read-only ones through, which is the difference
    /// between a useful macro surface and a dangerous one.
    /// </summary>
    public bool Mutates { get; init; }
}

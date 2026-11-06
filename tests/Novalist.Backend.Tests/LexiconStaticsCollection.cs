using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Isolates assertions about the process-wide lexicon directory, language list,
/// and cache. Every Workspace constructor registers its own analysis directory,
/// including constructors in test classes that do not otherwise use lexicons.
/// Sharing a collection only serializes its members; disabling parallelization
/// also prevents an unrelated Workspace from replacing the directory between a
/// rescan's registration and its language-pack report. Other collections can
/// still run in parallel with each other.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LexiconStaticsCollection
{
    public const string Name = "LexiconStatics";
}

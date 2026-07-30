using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Serializes every test class that reads or writes
/// <see cref="Novalist.Core.Services.SceneAnalysisLexicon"/>'s process-wide
/// state - its language list, its parsed-lexicon cache, and the registered user
/// directory.
///
/// The Core test project has had this since the same race was found there. This
/// project's classes touch the same statics through the RPCs and were never
/// grouped, so a class that drops a user lexicon into its own temp directory
/// could be observed mid-flight by a class asserting over the shipped set. It
/// failed roughly one run in four, always under the coverage collector, which
/// runs the suite slowly enough to widen the window.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LexiconStaticsCollection
{
    public const string Name = "LexiconStatics";
}

using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Serializes every test class that reads or writes
/// <see cref="Novalist.Core.Services.SceneAnalysisLexicon"/>'s process-wide
/// state — its language list, its parsed-lexicon cache, and the registered user
/// directory. xUnit runs distinct test classes in parallel, so without this
/// grouping a test that registers a user lexicon can be observed mid-flight by a
/// class asserting over the shipped set.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LexiconStaticsCollection
{
    public const string Name = "LexiconStatics";
}

namespace Novalist.Core.Services;

/// <summary>
/// How many printed pages a word count is worth.
///
/// Novalist could only answer this exactly, and only through one export preset:
/// the German Normseite, at 60 characters by 30 lines. That is the right answer
/// for a submission and the wrong one for the question people actually ask,
/// which is how thick the paperback will be. Every other tool in the field
/// estimates it from a words-per-page figure and says plainly that it is an
/// estimate.
/// </summary>
public static class PageEstimate
{
    /// <summary>
    /// The trade-paperback convention, and the default. Mass-market runs
    /// closer to 300 and a large-print edition nearer 150, which is why the
    /// figure is the writer's to set rather than ours to fix.
    /// </summary>
    public const int DefaultWordsPerPage = 250;

    /// <summary>
    /// Pages for a word count, rounded up - half a page of prose still costs a
    /// leaf of paper.
    /// </summary>
    /// <param name="wordsPerPage">
    /// Zero or less falls back to the default rather than dividing by nothing:
    /// a settings file edited by hand should not be able to break the count.
    /// </param>
    public static int Pages(int words, int wordsPerPage)
    {
        if (words <= 0) return 0;
        var perPage = wordsPerPage > 0 ? wordsPerPage : DefaultWordsPerPage;
        return (int)Math.Ceiling(words / (double)perPage);
    }
}

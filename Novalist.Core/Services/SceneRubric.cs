namespace Novalist.Core.Services;

/// <summary>Which part of the craft an element belongs to.</summary>
public static class RubricGroups
{
    public const string Character = "character";
    public const string Plot = "plot";
    public const string Setting = "setting";
}

/// <summary>
/// One named thing to look at in a scene, and what to do when it is weak.
/// </summary>
/// <param name="Question">
/// What the writer asks themselves. Phrased as a question because a rubric that
/// asserts is a rubric that argues.
/// </param>
/// <param name="Advice">
/// What to try when the answer is no. This is what makes a rubric teachable
/// rather than a scoreboard - a number with no next move is just a judgement.
/// </param>
public sealed record RubricElement(
    string Key,
    string Group,
    string Name,
    string Question,
    string Advice);

/// <summary>How one scene answers one element.</summary>
/// <param name="Score">
/// 1 to 5, or 0 for "not asked here" - a chase scene is not failing at
/// interiority, it is not trying, and a rubric that cannot say so scores every
/// action scene as broken.
/// </param>
public sealed record RubricScore(string ElementKey, int Score);

/// <summary>A scene and how much of the rubric it has been answered against.</summary>
public sealed record RubricSceneSummary(
    string ChapterGuid,
    string SceneId,
    int Answered,
    int Weak,
    double Average);

/// <summary>
/// A named rubric for reading a scene, with advice bound to each element.
///
/// Novalist's per-scene analysis is descriptive - point of view, emotion,
/// intensity, tags - which says what a scene is and never whether it works.
/// A writer looking at a weak scene got numbers and no next move, and a
/// judgement with no next move is not teaching, it is marking.
///
/// The elements are deliberately few and named in plain words. Thirty-eight
/// boxes is a form; a dozen questions is something a writer will actually
/// answer on the scene in front of them.
/// </summary>
public static class SceneRubric
{
    /// <summary>Not asked in this scene, as opposed to asked and failed.</summary>
    public const int NotAsked = 0;

    /// <summary>At or below this, an element is worth the writer's attention.</summary>
    public const int WeakAtOrBelow = 2;

    /// <summary>
    /// The elements, in the order a writer would read them: who, then what
    /// happens, then where.
    /// </summary>
    public static IReadOnlyList<RubricElement> Elements { get; } =
    [
        new("goal", RubricGroups.Character, "Wants something",
            "Does the point-of-view character want something in this scene, now, that a reader could name?",
            "Name it in one sentence before rewriting. If it cannot be named, the scene is usually a transition wearing a scene's clothes - either give the character an errand or fold the scene into its neighbour."),

        new("obstacle", RubricGroups.Plot, "Something is in the way",
            "Is something or somebody stopping them getting it?",
            "A scene where the want is granted is a delivery. Put a person in the way rather than a circumstance where you can: weather is an obstacle, an opinion is a scene."),

        new("stakes", RubricGroups.Plot, "It costs something",
            "Does the reader know what it costs to fail here?",
            "Say what is lost, once, early, in the character's own terms. Stakes stated in the abstract - the kingdom, the mission - carry less than one concrete thing the character will not get back."),

        new("change", RubricGroups.Plot, "Something changes",
            "Is the situation different at the end than at the start?",
            "Find the sentence where it turns. If there is not one, decide what the scene is for and let something break, be learned, or be decided in it."),

        new("interiority", RubricGroups.Character, "We are inside somebody",
            "Do we get what this is like from the inside, not only what happened?",
            "One line of thought at the moment of pressure does more than a paragraph of reflection afterwards. Put it where the character has to decide."),

        new("voice", RubricGroups.Character, "It sounds like them",
            "Would this scene read differently if another character had the point of view?",
            "Cut two lines of narration and rewrite them in words this character would use. If nothing changes, the narration is yours rather than theirs."),

        new("conflict", RubricGroups.Character, "Nobody agrees",
            "Do the people in this scene want different things?",
            "Give the second character a reason to be in the room that is not the first character's need. Two people cooperating is information delivery."),

        new("grounding", RubricGroups.Setting, "It happens somewhere",
            "Could a reader say where this is by the end of the first page?",
            "Two specifics beat a paragraph of description, and specifics the point-of-view character would notice beat accurate ones."),

        new("senses", RubricGroups.Setting, "More than sight",
            "Is anything heard, smelled, touched or tasted here?",
            "Almost everybody writes eyes and ears only. One smell or one texture per scene is usually the whole fix."),

        new("time", RubricGroups.Setting, "It happens when",
            "Does the reader know when this is, relative to the scene before?",
            "A half-line will do it. The reader is tracking a chronology whether you help them or not."),

        new("entrance", RubricGroups.Plot, "It starts late",
            "Does the scene start at the last possible moment?",
            "Cut until the first line is doing work. Arrivals, greetings and sitting down are almost always cuttable."),

        new("exit", RubricGroups.Plot, "It leaves early",
            "Does the scene end before the reader is finished with it?",
            "End on the turn, not on the tidying up. If the last paragraph explains what just happened, cut it."),
    ];

    /// <summary>An element by key, or null.</summary>
    public static RubricElement? Find(string key)
        => Elements.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Reads a scene's stored scores. Anything that is not an element the
    /// rubric has, or not a score it understands, is dropped rather than shown
    /// - a stale key from an older rubric must not appear as a nameless row.
    /// </summary>
    public static IReadOnlyList<RubricScore> Read(IReadOnlyDictionary<string, string>? stored)
    {
        if (stored == null) return [];
        var scores = new List<RubricScore>();
        foreach (var element in Elements)
        {
            if (!stored.TryGetValue(Key(element.Key), out var raw)) continue;
            if (!int.TryParse(raw, out var score) || score is < NotAsked or > 5) continue;
            scores.Add(new RubricScore(element.Key, score));
        }
        return scores;
    }

    /// <summary>
    /// Writes one score into a scene's own property bag. A score of
    /// <see cref="NotAsked"/> removes it: "not asked" is the absence of an
    /// answer, and storing it would make an untouched scene indistinguishable
    /// from one somebody has been through.
    /// </summary>
    public static void Write(Dictionary<string, string> properties, string elementKey, int score)
    {
        if (Find(elementKey) == null) return;
        if (score is < NotAsked or > 5) return;

        if (score == NotAsked) properties.Remove(Key(elementKey));
        else properties[Key(elementKey)] = score.ToString();
    }

    /// <summary>
    /// How far through the rubric a scene is, and how much of it is weak.
    /// </summary>
    public static RubricSceneSummary Summarise(
        string chapterGuid, string sceneId, IReadOnlyDictionary<string, string>? stored)
    {
        var scores = Read(stored).Where(s => s.Score != NotAsked).ToList();
        return new RubricSceneSummary(
            chapterGuid,
            sceneId,
            scores.Count,
            scores.Count(s => s.Score <= WeakAtOrBelow),
            // Zero rather than a divide by nothing: a scene nobody has read
            // against the rubric has no average, and pretending it scores zero
            // would sort it beside the worst scenes in the book.
            scores.Count == 0 ? 0 : scores.Average(s => s.Score));
    }

    /// <summary>
    /// The property key one element is stored under. Prefixed, because a scene's
    /// property bag also holds fields the writer invented and a rubric key
    /// colliding with one of those would overwrite their work.
    /// </summary>
    private static string Key(string elementKey) => $"rubric:{elementKey}";
}

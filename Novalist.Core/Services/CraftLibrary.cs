namespace Novalist.Core.Services;

/// <summary>One thing to write, and what kind of thing it is.</summary>
/// <param name="Kind">
/// "scene", "character", "world" or "stuck" - so a writer wanting a way back
/// into a stalled chapter is not handed a character-building exercise.
/// </param>
public sealed record CraftPrompt(string Id, string Kind, string Text);

/// <summary>
/// One entry of the description thesaurus: a thing to describe, and the
/// specifics a writer reaches for when they cannot think of any.
/// </summary>
/// <param name="Signals">
/// Concrete, observable details. Never adjectives for the thing itself - "she
/// was frightened" is the sentence the thesaurus exists to replace.
/// </param>
public sealed record CraftEntry(string Key, string Group, string Name, IReadOnlyList<string> Signals);

/// <summary>A short piece on the craft, meant to be read while a scene is open.</summary>
/// <param name="Body">
/// Plain text with blank lines between paragraphs. No markup: this is read in
/// a panel, not published, and a format nobody can render is a format.
/// </param>
public sealed record CraftArticle(string Id, string Topic, string Title, string Body)
{
    /// <summary>
    /// The article text, with line endings normalised to <c>\n</c>.
    ///
    /// The bodies are literals in this file, so their line endings are whatever
    /// the checkout produced: a Windows clone gets CRLF and the blank line
    /// between two paragraphs stops being "\n\n". Anything splitting on it then
    /// reads the whole article as one paragraph, on some machines and not
    /// others. What a paragraph break is should not depend on who cloned the
    /// repository.
    /// </summary>
    public string Body { get; init; } = Body.Replace("\r\n", "\n");
}

/// <summary>
/// Reference a writer can reach without leaving the app.
///
/// Novalist's lexicons were machine-readable analysis stems, never shown to
/// anybody: the app could count filter words and could not help a writer find
/// a better one. So a blank page had nothing behind it and a scene that needed
/// a body-language beat sent them to a browser, which is where writing
/// sessions go to die.
///
/// Deliberately small and concrete. A thousand entries is a product nobody
/// reads; forty that are specific enough to steal from is a tool.
/// </summary>
public static class CraftLibrary
{
    public const string KindScene = "scene";
    public const string KindCharacter = "character";
    public const string KindWorld = "world";
    public const string KindStuck = "stuck";

    public const string GroupEmotion = "emotion";
    public const string GroupSetting = "setting";
    public const string GroupSense = "sense";

    /// <summary>Something to write when the page is blank.</summary>
    public static IReadOnlyList<CraftPrompt> Prompts { get; } =
    [
        new("p1", KindScene, "Two people who need the same thing, and only one of them knows it."),
        new("p2", KindScene, "A conversation where neither says the thing it is about."),
        new("p3", KindScene, "Somebody arrives an hour too late and finds out what that cost."),
        new("p4", KindScene, "A character does something kind for a reason that is not kind."),
        new("p5", KindScene, "The last time these two were in this room, something else happened."),
        new("p6", KindScene, "An apology that makes things worse."),
        new("p7", KindScene, "Somebody is handed a thing they have wanted for years."),
        new("p8", KindScene, "A promise made in front of witnesses who do not believe it."),
        new("p9", KindScene, "A character notices they are being lied to and says nothing."),
        new("p10", KindScene, "Two people wait. One of them knows what for."),

        new("p11", KindCharacter, "What does this character do with their hands when they lie?"),
        new("p12", KindCharacter, "Write the sentence they would never say aloud."),
        new("p13", KindCharacter, "What did they want at twelve, and what became of it?"),
        new("p14", KindCharacter, "Who taught them the thing they are best at, and are they still speaking?"),
        new("p15", KindCharacter, "What would their oldest friend warn a stranger about?"),
        new("p16", KindCharacter, "What are they wrong about, and who has tried to tell them?"),
        new("p17", KindCharacter, "Where do they go when they cannot be at home?"),
        new("p18", KindCharacter, "What do they own that is worth nothing and would not be sold?"),

        new("p19", KindWorld, "What does this place smell of at four in the morning?"),
        new("p20", KindWorld, "Who cleans up after the thing everyone here takes for granted?"),
        new("p21", KindWorld, "What is the local argument that outsiders find baffling?"),
        new("p22", KindWorld, "What did this place used to be, and who still calls it that?"),
        new("p23", KindWorld, "What is illegal here that is ordinary elsewhere?"),
        new("p24", KindWorld, "What sound would a returning native recognise before they saw anything?"),
        new("p25", KindWorld, "Who has money here, and what did they do to get it?"),

        new("p26", KindStuck, "Write the next scene from the point of view of whoever is least happy about it."),
        new("p27", KindStuck, "Cut the first three paragraphs. Does anything break?"),
        new("p28", KindStuck, "Let the character say the thing you have been having them avoid."),
        new("p29", KindStuck, "Skip the part you are dreading and write the scene after it."),
        new("p30", KindStuck, "Ask what would happen if the character simply failed here."),
        new("p31", KindStuck, "Give somebody in the scene a reason to want it over with."),
        new("p32", KindStuck, "Write the same page in first person, then decide."),
        new("p33", KindStuck, "Name what the reader is waiting for. Are you making them wait for it, or from it?"),
    ];

    /// <summary>
    /// Specifics for the things writers describe most and reach for least.
    /// </summary>
    public static IReadOnlyList<CraftEntry> Entries { get; } =
    [
        new("fear", GroupEmotion, "Fear",
            ["a swallow that will not go down", "hands that need something to hold",
             "hearing their own pulse", "checking the exit without meaning to",
             "a voice that comes out too level", "the cold that starts at the wrists"]),
        new("anger", GroupEmotion, "Anger",
            ["a very quiet voice", "putting things down too carefully",
             "an unfinished sentence", "looking somewhere else for a long moment",
             "a smile with nothing behind it", "answering a different question"]),
        new("grief", GroupEmotion, "Grief",
            ["doing a small task perfectly", "the flatness of the third day",
             "flinching at a name in an ordinary sentence", "hunger that arrives and is refused",
             "sleeping at the wrong times", "keeping the message unread"]),
        new("shame", GroupEmotion, "Shame",
            ["heat at the back of the neck", "an apology nobody asked for",
             "agreeing too fast", "finding a reason to leave the room",
             "rehearsing what should have been said", "answering a joke seriously"]),
        new("love", GroupEmotion, "Love",
            ["knowing how they take their tea", "not needing the sentence finished",
             "checking the weather where they are", "keeping the good chair for them",
             "reading the room on their behalf", "a hand that lands without deciding to"]),
        new("relief", GroupEmotion, "Relief",
            ["laughing at nothing", "the legs going", "suddenly noticing hunger",
             "talking too much", "the shoulders arriving an inch lower",
             "an old joke, badly told"]),
        new("exhaustion", GroupEmotion, "Exhaustion",
            ["reading the same line three times", "arithmetic that will not come",
             "hearing a question and not the words", "being cold in a warm room",
             "sitting down to do one thing and doing none", "a temper with no cause under it"]),
        new("suspicion", GroupEmotion, "Suspicion",
            ["a question asked twice, differently", "watching the hands rather than the face",
             "agreement that arrives too late", "remembering exactly what was said",
             "a friendliness with edges", "declining a small favour"]),

        new("morning", GroupSetting, "Early morning",
            ["a road with one car on it", "the smell of somebody else's cooking",
             "birds, then a lorry", "light the colour of weak tea",
             "shutters going up", "breath visible over a doorstep"]),
        new("night", GroupSetting, "Night",
            ["a window lit in a dark row", "sodium light on wet tarmac",
             "an argument two floors down", "the fridge, then nothing",
             "a taxi that does not stop", "somebody laughing a street away"]),
        new("crowd", GroupSetting, "A crowd",
            ["being moved without walking", "somebody's bag against the hip",
             "one voice you keep hearing", "the smell of wet coats",
             "a child at the height of everyone's elbows", "everyone facing the same way"]),
        new("empty-room", GroupSetting, "An empty room",
            ["dust in a shaft of light", "the shape a picture left",
             "a chair not quite square to the table", "the sound of your own shoes",
             "cold that has been there a while", "a smell that belongs to somebody else"]),
        new("weather", GroupSetting, "Weather that matters",
            ["rain heavy enough to be a sound", "wind that changes what people say",
             "heat that empties a street", "the light before a storm",
             "snow that makes the town quiet", "the first cold morning of the year"]),
        new("water", GroupSetting, "Water",
            ["a river the colour of the sky above it", "the smell before you see it",
             "a surface that is not still", "cold at the ankles and nowhere else",
             "the noise it makes against stone", "what it has left on the bank"]),

        new("sound", GroupSense, "What can be heard",
            ["a room's own hum", "somebody breathing nearby", "a door two rooms away",
             "the silence after a machine stops", "cloth against cloth",
             "the pitch a voice goes when it is being careful"]),
        new("smell", GroupSense, "What can be smelled",
            ["cold stone", "someone else's soap", "burnt dust off a heater",
             "rain on hot pavement", "old paper", "food from a window above"]),
        new("touch", GroupSense, "What can be felt",
            ["a table's grain under the fingers", "damp in a sleeve",
             "the give of an old floor", "warmth left in a chair",
             "grit underfoot", "a handle worn smooth"]),
        new("taste", GroupSense, "What can be tasted",
            ["metal after a fright", "tea gone cold", "salt on the lips",
             "the dust of a dry day", "blood from a bitten cheek", "something sweeter than expected"]),
    ];

    /// <summary>
    /// Short pieces on the craft, written to be read in the app while a scene
    /// is open rather than saved for later.
    ///
    /// Each one is about something Novalist already models - point of view,
    /// stakes, scene structure, revision - so reading it and using the app are
    /// the same motion. A craft library that talks about things the app has no
    /// idea about is a blog with a worse reader.
    /// </summary>
    public static IReadOnlyList<CraftArticle> Articles { get; } =
    [
        new("pov-distance", "Point of view",
            "How close are we standing?",
            """
            Point of view is not only whose head we are in. It is how far away
            we stand from them, and that distance is a dial rather than a
            setting.

            At the far end the narrator knows things the character does not:
            "She would remember that morning for the rest of her life." At the
            near end there is no narrator at all, only the character's own
            attention: "The tea had gone cold. Again."

            Most drafts pick a distance by accident and drift. The reader
            cannot say what is wrong, only that the scene feels off. What they
            are feeling is the camera moving without being asked to.

            The practical test: read a page and mark every sentence the
            character could not have thought. If there are two, you are close
            in. If there are twenty, you are standing well back. Either is
            fine. Both on the same page usually is not.

            Move deliberately and the dial becomes a tool. Pull back for the
            years passing in a paragraph; go close for the hand on the door.
            """),

        new("stakes-concrete", "Stakes",
            "Say what is actually lost",
            """
            Stakes stated in the abstract do almost no work. The kingdom, the
            mission, everything she has ever cared about - a reader nods and
            feels nothing, because none of it is a thing they can picture being
            taken away.

            Concrete stakes are smaller and hit harder. Not "she could lose
            everything" but "she will have to tell her daughter they are
            moving again." Not "the city falls" but "the man who runs the shop
            on the corner will be dead by Thursday."

            The reason is simple: a reader cannot grieve for an abstraction.
            They can grieve for a shop.

            The other half is timing. Stakes work when the reader knows them
            before the danger, not after. A scene that reveals what was at
            risk once it is lost has written a summary of a tense scene rather
            than a tense scene.

            Say it once, early, in the character's own terms, and then never
            mention it again. Repetition does not raise stakes; it tells the
            reader you do not trust them.
            """),

        new("scene-shape", "Scene structure",
            "A scene is a small argument",
            """
            The most useful shape for a scene is not three acts. It is: somebody
            wants something, something is in the way, and by the end the
            situation has changed.

            Notice what is missing. Nothing about what the scene is "about".
            Nothing about theme. A scene can carry as much theme as you like,
            but it cannot carry it if nothing is happening.

            The commonest broken scene is the one where two people who agree
            exchange information. It reads as flat because it is flat: no
            want, no obstacle, nothing different at the end. The usual fix is
            not more description. It is giving the second person a reason to be
            in the room that is not the first person's need.

            The second commonest is the scene that starts too early. Arrivals,
            greetings, sitting down, ordering. Cut until the first line is
            doing work, and trust the reader to have understood that somebody
            walked through a door.

            And end on the turn. If your last paragraph explains what just
            happened, delete it. The reader was there.
            """),

        new("dialogue-subtext", "Dialogue",
            "What they are not saying",
            """
            People rarely say what they mean, and almost never in the order
            they mean it. Dialogue that reads as real is usually dialogue where
            the actual subject is underneath.

            The technique is simple to state and hard to do: give the
            conversation a surface topic that is not the real one. Two people
            argue about a car and are arguing about money. A parent asks about
            work and is asking whether their child is all right.

            The reader does the arithmetic and feels clever, which is one of
            the great pleasures of reading fiction.

            Two things break it. The first is a character who says the true
            thing out loud in the middle, which ends the scene four paragraphs
            early. The second is subtext so buried that nobody can find it -
            leave a handhold, usually one line where somebody almost says it.

            Attribution: "said" is invisible and you cannot use it too often.
            Anything more decorative asks the reader to notice the writer.
            """),

        new("description-specific", "Description",
            "Two specifics beat a paragraph",
            """
            A paragraph of description is usually the writer proving they can
            see the room. Two well-chosen details are the reader seeing it,
            which is the only version that counts.

            The choosing is the craft. A detail earns its place when it is
            something this point-of-view character would notice, in this mood,
            at this moment. A grieving woman does not see the architecture. She
            sees that someone has moved the chair.

            This is also why accurate description often reads as dead. It is
            correct and unowned. The specific that is slightly wrong but
            clearly seen through somebody beats the correct one every time.

            The other habit worth breaking is writing only what can be seen.
            Almost every draft is eyes and ears. One smell or one texture per
            scene is usually the whole fix, and it is the fastest way to make
            a place feel like somewhere rather than like a description.
            """),

        new("revision-order", "Revision",
            "Big things first, sentences last",
            """
            The commonest way to waste a month is to polish sentences in a
            scene you are going to cut.

            Revise in order of what is expensive to change. Structure first:
            is this scene needed, is it in the right place, does the book work
            without it. Then the scene: does somebody want something, is
            something in the way, does anything change. Then the paragraph.
            Only then the sentence.

            Working the other way round feels productive - line edits are
            visible and finite - and it is how a manuscript ends up beautifully
            written and structurally broken. It also makes the big cuts harder,
            because now they cost something.

            A useful discipline: on the structural pass, do not open the prose
            at all. Work from the outline, the synopses and the scene list.
            You are asking whether the shape holds, and the prose will argue
            with you.

            When you do reach the sentences, one pass per problem beats one
            pass for everything. Read once for filter words. Once for repeated
            openings. Once aloud.
            """),

        new("first-draft-speed", "Drafting",
            "The first draft is not the book",
            """
            A first draft's only job is to exist. It is you finding out what
            the story is, and the version of it that reaches a reader will be
            built from what you learn writing it - not from the sentences
            themselves.

            This is why speed helps more than it should. A draft written
            quickly is more coherent than one written slowly, because you can
            still remember chapter four when you are writing chapter twenty.
            The slow draft is not more careful. It is a different book every
            eighty pages.

            The practical consequence: leave the holes. When you cannot think
            of the name, write NAME. When you do not know whether it rains,
            say it rains. When a scene will not come, write one line about what
            happens in it and go on to the next.

            The instinct to fix it now is the instinct to stop. Note it and
            keep moving; you will be a better writer when you come back,
            because you will have written a book in between.
            """),

        new("character-want", "Character",
            "Want, need, and the gap between",
            """
            The most durable engine for a character is a want they can name and
            a need they cannot.

            The want is what they would say if you asked: the job, the
            revenge, the person. It drives the plot because it makes them do
            things. The need is what would actually make their life bearable,
            and they are usually the last to see it.

            The gap between them is where the character arc lives. A story
            where a character gets what they wanted and it turns out to be the
            wrong thing is one of the few shapes that never gets old.

            The mistake worth avoiding: making the need too tidy. "He needs to
            learn to trust" is a lesson, not a life. "He needs to stop
            apologising to his father, who has been dead for six years" is a
            person.

            And a character can be wrong about their want, too. What they say
            they are chasing and what they actually chase, scene by scene, is
            one of the sharpest tools you have.
            """),
    ];

    /// <summary>
    /// A prompt, chosen by a number the caller gives. The caller owns the
    /// randomness so the same number gives the same prompt - a writer who liked
    /// one can get it back, and a test can say which one it expects.
    /// </summary>
    public static CraftPrompt? PromptAt(int index, string? kind = null)
    {
        var pool = string.IsNullOrEmpty(kind)
            ? Prompts
            : [.. Prompts.Where(p => string.Equals(p.Kind, kind, StringComparison.Ordinal))];
        if (pool.Count == 0) return null;

        // Wraps, and handles a negative, so no caller has to think about it.
        var at = ((index % pool.Count) + pool.Count) % pool.Count;
        return pool[at];
    }

    /// <summary>
    /// Entries matching a query, by name or by any of their signals. Empty
    /// query returns everything, because the list is short enough to browse and
    /// browsing is how a writer finds the one they did not know to look for.
    /// </summary>
    public static IReadOnlyList<CraftEntry> Search(string? query)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0) return Entries;

        return [.. Entries.Where(e =>
            e.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || e.Key.Contains(text, StringComparison.OrdinalIgnoreCase)
            || e.Signals.Any(s => s.Contains(text, StringComparison.CurrentCultureIgnoreCase)))];
    }

    /// <summary>One article by id, or null.</summary>
    public static CraftArticle? Article(string id)
        => Articles.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
}

using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Represents a single book within a Novalist project.
/// Each book has its own chapters, scenes, entities, and templates.
/// </summary>
public class BookData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("folderName")]
    public string FolderName { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("chapters")]
    public List<ChapterData> Chapters { get; set; } = new();

    [JsonPropertyName("chapterFolder")]
    public string ChapterFolder { get; set; } = "Chapters";

    [JsonPropertyName("characterFolder")]
    public string CharacterFolder { get; set; } = "Characters";

    [JsonPropertyName("locationFolder")]
    public string LocationFolder { get; set; } = "Locations";

    [JsonPropertyName("itemFolder")]
    public string ItemFolder { get; set; } = "Items";

    [JsonPropertyName("loreFolder")]
    public string LoreFolder { get; set; } = "Lore";

    [JsonPropertyName("imageFolder")]
    public string ImageFolder { get; set; } = "Images";

    [JsonPropertyName("snapshotFolder")]
    public string SnapshotFolder { get; set; } = "Snapshots";

    /// <summary>Portrait book-cover image (shown on the welcome/start screen).
    /// Stored as a book-root-relative path.</summary>
    [JsonPropertyName("coverImage")]
    public string CoverImage { get; set; } = string.Empty;

    /// <summary>Wide banner image (shown on the Dashboard). Stored as a
    /// book-root-relative path. When empty, the Dashboard falls back to
    /// <see cref="CoverImage"/> so pre-split projects keep their banner.</summary>
    [JsonPropertyName("bannerImage")]
    public string BannerImage { get; set; } = string.Empty;

    /// <summary>
    /// Scenes worth starting from. Stored with the book because a scene form
    /// belongs to a book's way of working, not to the writer's installation.
    /// </summary>
    [JsonPropertyName("sceneTemplates")]
    public List<SceneTemplate> SceneTemplates { get; set; } = [];

    [JsonPropertyName("characterTemplates")]
    public List<CharacterTemplate> CharacterTemplates { get; set; } = [];

    [JsonPropertyName("locationTemplates")]
    public List<LocationTemplate> LocationTemplates { get; set; } = [];

    [JsonPropertyName("itemTemplates")]
    public List<ItemTemplate> ItemTemplates { get; set; } = [];

    [JsonPropertyName("loreTemplates")]
    public List<LoreTemplate> LoreTemplates { get; set; } = [];

    [JsonPropertyName("activeCharacterTemplateId")]
    public string ActiveCharacterTemplateId { get; set; } = string.Empty;

    [JsonPropertyName("activeLocationTemplateId")]
    public string ActiveLocationTemplateId { get; set; } = string.Empty;

    [JsonPropertyName("activeItemTemplateId")]
    public string ActiveItemTemplateId { get; set; } = string.Empty;

    [JsonPropertyName("activeLoreTemplateId")]
    public string ActiveLoreTemplateId { get; set; } = string.Empty;

    /// <summary>
    /// Templates for custom entity types, shared across all custom types in this book.
    /// </summary>
    [JsonPropertyName("customEntityTemplates")]
    public List<CustomEntityTemplate> CustomEntityTemplates { get; set; } = [];

    /// <summary>
    /// Active template ID per custom entity type key.
    /// </summary>
    [JsonPropertyName("activeCustomEntityTemplateIds")]
    public Dictionary<string, string> ActiveCustomEntityTemplateIds { get; set; } = [];

    /// <summary>Plot threads defined for this book. Drives the Plot Grid view.</summary>
    [JsonPropertyName("plotlines")]
    public List<PlotlineData> Plotlines { get; set; } = [];

    /// <summary>
    /// Where this book has been sent and what came back.
    ///
    /// Novalist produced submission-ready material and recorded nothing about
    /// where it went, so the one thing a writer must not do - send the same
    /// manuscript to the same agent twice - was the one thing it could not
    /// help with.
    /// </summary>
    [JsonPropertyName("submissions")]
    public List<Submission> Submissions { get; set; } = [];

    /// <summary>Optional per-act metadata (date ranges etc.). Acts referenced
    /// by name from <see cref="ChapterData.Act"/>.</summary>
    [JsonPropertyName("acts")]
    public List<ActData> Acts { get; set; } = [];

    /// <summary>In-world calendar configuration.</summary>
    [JsonPropertyName("calendar")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InWorldCalendar? Calendar { get; set; }

    /// <summary>
    /// The stages scenes in this book can be at. Per book rather than per app,
    /// because a writer's stages for a novel and for a short-story collection
    /// are rarely the same. Empty means the defaults.
    /// </summary>
    [JsonPropertyName("sceneStages")]
    public List<SceneStage> SceneStages { get; set; } = [];

    /// <summary>
    /// Labels a scene in this book can carry. Per book, like the stages: what
    /// is worth flagging in a thriller and in a collection differ.
    /// </summary>
    [JsonPropertyName("sceneLabels")]
    public List<SceneLabel> SceneLabels { get; set; } = [];

    /// <summary>
    /// Hand-curated scene sets. A saved list is a query and recomputes; a
    /// collection is the eight scenes to fix before Tuesday, which no filter
    /// can describe - that is why they had to be gathered by hand.
    /// </summary>
    [JsonPropertyName("collections")]
    public List<SceneCollection> Collections { get; set; } = [];

    /// <summary>
    /// Words and phrases this book completes as you type. The @-mention picker
    /// reaches Codex names in scene prose and nothing else, which leaves out
    /// every coined word, rank and settled spelling the Codex does not hold.
    /// </summary>
    [JsonPropertyName("completions")]
    public CompletionList Completions { get; set; } = new();

    /// <summary>
    /// Factions, houses, crews and families. The group was a bare string on
    /// each entry with no colour, description or rename, so correcting "the
    /// Ravens" to "House Raven" meant opening every entry that said the first.
    /// </summary>
    [JsonPropertyName("groups")]
    public List<EntityGroup> Groups { get; set; } = [];

    /// <summary>
    /// Fields the writer added to every scene or chapter of this book. Per book
    /// for the same reason the stages are: the things worth tracking differ
    /// between a thriller and a short-story collection.
    /// </summary>
    [JsonPropertyName("manuscriptProperties")]
    public List<ManuscriptPropertyDefinition> ManuscriptProperties { get; set; } = [];

    /// <summary>
    /// The book in one line, one paragraph and one summary per act. Per book:
    /// the second book of a series has its own premise, not the first one's.
    /// </summary>
    [JsonPropertyName("premise")]
    public StoryPremise Premise { get; set; } = new();

    /// <summary>What a shop, a library and a distributor need to know about this
    /// book. Written into the EPUB metadata block on export.</summary>
    [JsonPropertyName("publishing")]
    public PublishingMetadata Publishing { get; set; } = new();

    /// <summary>
    /// Substitutions applied to every export of this book and never to the
    /// prose. Stored on the book because "the submission copy spells it out and
    /// the ebook uses the glyph" is a decision about one book.
    /// </summary>
    [JsonPropertyName("exportReplacements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ExportReplacement> ExportReplacements { get; set; } = [];

    /// <summary>
    /// The narrative person the book is written in: "first", "second", "third
    /// limited", "third omniscient", or empty when the writer has not said.
    ///
    /// Novalist detects a point of view per scene and always has, but nothing
    /// declared what the book as a whole was supposed to be - so a first-person
    /// novel with one third-person scene in it had nothing to be wrong against.
    /// Declared rather than derived: the answer is the writer's intention, and
    /// deriving it from the majority of scenes would make the outlier normal.
    /// </summary>
    [JsonPropertyName("narrativePerson")]
    public string NarrativePerson { get; set; } = string.Empty;

    /// <summary>
    /// The tense the book is written in: "past", "present", or empty. Same
    /// reasoning as <see cref="NarrativePerson"/>, and the same use - a scene
    /// that drifts out of it can be told about.
    /// </summary>
    [JsonPropertyName("tense")]
    public string Tense { get; set; } = string.Empty;

    /// <summary>
    /// Id of the story structure this book is written against, or empty for
    /// none. Chosen rather than derived: a writer using Save the Cat should not
    /// have Novalist guess that from their chapter count.
    /// </summary>
    [JsonPropertyName("structureTemplateId")]
    public string StructureTemplateId { get; set; } = string.Empty;

    /// <summary>
    /// Export layouts the writer authored. Stored on the book rather than
    /// globally: a submission format for a novel and one for a short-story
    /// collection have no reason to share a list.
    /// </summary>
    [JsonPropertyName("exportPresets")]
    public List<ExportPreset> ExportPresets { get; set; } = [];

    /// <summary>Named drafts of this book. The active draft's chapters / acts
    /// live in BookData.Chapters / Acts at runtime; on switch, the outgoing
    /// draft is flushed to draft.json and the incoming draft loaded.</summary>
    [JsonPropertyName("drafts")]
    public List<BookDraftMetadata> Drafts { get; set; } = new();

    [JsonPropertyName("activeDraftId")]
    public string ActiveDraftId { get; set; } = string.Empty;

    /// <summary>
    /// Chapters deleted from the active draft, newest first. Per-draft like
    /// the chapters themselves: emptying the trash in one draft has no business
    /// touching another.
    /// </summary>
    [JsonIgnore]
    public List<ChapterData> Trash { get; set; } = [];

    [JsonIgnore]
    public BookDraftMetadata? ActiveDraft
        => Drafts.FirstOrDefault(d => string.Equals(d.Id, ActiveDraftId, StringComparison.OrdinalIgnoreCase))
           ?? Drafts.FirstOrDefault();

    /// <summary>Interactive map references. Map content itself is stored in
    /// per-map JSON under the active draft's Maps/ folder.</summary>
    [JsonPropertyName("maps")]
    public List<MapReference> Maps { get; set; } = new();

    /// <summary>Planning-board references. Board content is stored in per-board
    /// JSON under the active draft's Canvases/ folder, the same way maps are.</summary>
    [JsonPropertyName("canvases")]
    public List<CanvasReference> Canvases { get; set; } = new();

    /// <summary>
    /// Front- and back-matter pages: half title, copyright, dedication,
    /// acknowledgments and so on. Stored on the book rather than as chapters so
    /// each kind can be laid out its own way in an export.
    /// </summary>
    [JsonPropertyName("matter")]
    public List<BookMatterElement> Matter { get; set; } = new();

    /// <summary>Character budget for this book's exposé, spaces included.
    /// 0 means no limit. The exposé editor warns past it but never blocks.</summary>
    [JsonPropertyName("exposeCharLimit")]
    public int ExposeCharLimit { get; set; }

    /// <summary>Normseiten budget for this book's exposé. 0 means no limit.</summary>
    [JsonPropertyName("exposePageLimit")]
    public int ExposePageLimit { get; set; }
}

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

    /// <summary>What a shop, a library and a distributor need to know about this
    /// book. Written into the EPUB metadata block on export.</summary>
    [JsonPropertyName("publishing")]
    public PublishingMetadata Publishing { get; set; } = new();

    /// <summary>
    /// Id of the story structure this book is written against, or empty for
    /// none. Chosen rather than derived: a writer using Save the Cat should not
    /// have Novalist guess that from their chapter count.
    /// </summary>
    [JsonPropertyName("structureTemplateId")]
    public string StructureTemplateId { get; set; } = string.Empty;

    /// <summary>Named drafts of this book. The active draft's chapters / acts
    /// live in BookData.Chapters / Acts at runtime; on switch, the outgoing
    /// draft is flushed to draft.json and the incoming draft loaded.</summary>
    [JsonPropertyName("drafts")]
    public List<BookDraftMetadata> Drafts { get; set; } = new();

    [JsonPropertyName("activeDraftId")]
    public string ActiveDraftId { get; set; } = string.Empty;

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

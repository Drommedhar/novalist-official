using Novalist.Core.Models;
using Novalist.Desktop.Editor;
using Xunit;

namespace Novalist.Desktop.Tests.Editor;

public class EditorExtensionsTests
{
    private sealed class FakeExt : IEditorExtension
    {
        public string Name { get; init; } = "Fake";
        public int Priority { get; init; } = 100;
        public int Opened, Closing;
        public void OnDocumentOpened(EditorDocumentContext c) => Opened++;
        public void OnDocumentClosing(EditorDocumentContext c) => Closing++;
    }

    private static EditorDocumentContext Ctx() => new()
    {
        SceneId = "s", ChapterGuid = "c", SceneTitle = "T", ChapterTitle = "Ch", FilePath = "x.html",
    };

    // ── EditorExtensionManager ──────────────────────────────────────
    [Fact]
    public void Manager_RegisterSortsByPriority()
    {
        var mgr = new EditorExtensionManager();
        var high = new FakeExt { Name = "High", Priority = 200 };
        var low = new FakeExt { Name = "Low", Priority = 10 };
        mgr.Register(high);
        mgr.Register(low);
        Assert.Equal("Low", mgr.Extensions[0].Name); // sorted ascending
        Assert.Equal(2, mgr.Extensions.Count);
    }

    [Fact]
    public void Manager_NotifyOpenClose_AndRegisterAfterOpen()
    {
        var mgr = new EditorExtensionManager();
        var a = new FakeExt();
        mgr.Register(a);
        mgr.NotifyDocumentOpened(Ctx());
        Assert.Equal(1, a.Opened);

        // Register while a document is open -> immediately notified.
        var b = new FakeExt();
        mgr.Register(b);
        Assert.Equal(1, b.Opened);

        mgr.NotifyDocumentClosing();
        Assert.Equal(1, a.Closing);
        Assert.Equal(1, b.Closing);

        mgr.NotifyDocumentClosing(); // no current context -> no-op
        Assert.Equal(1, a.Closing);
    }

    [Fact]
    public void Manager_Unregister_NotifiesClosingWhenOpen()
    {
        var mgr = new EditorExtensionManager();
        var a = new FakeExt();
        mgr.Register(a);
        mgr.NotifyDocumentOpened(Ctx());
        mgr.Unregister(a);
        Assert.Equal(1, a.Closing);
        Assert.Empty(mgr.Extensions);
    }

    [Fact]
    public void Manager_Shutdown_ClosesAndClears()
    {
        var mgr = new EditorExtensionManager();
        var a = new FakeExt();
        mgr.Register(a);
        mgr.NotifyDocumentOpened(Ctx());
        mgr.Shutdown();
        Assert.Equal(1, a.Closing);
        Assert.Empty(mgr.Extensions);

        // Shutdown again with no context -> no throw.
        new EditorExtensionManager().Shutdown();
    }

    // ── AutoReplacementExtension ────────────────────────────────────
    [Fact]
    public void AutoReplacement_PairsAndSerialize()
    {
        var ext = new AutoReplacementExtension();
        Assert.Equal("AutoReplacement", ext.Name);
        Assert.Equal(50, ext.Priority);
        Assert.Equal("[]", ext.SerializePairsJson());

        ext.Pairs = null!; // null -> empty
        Assert.Empty(ext.Pairs);

        ext.Pairs =
        [
            new AutoReplacementPair { Start = "\"", End = "\"", StartReplace = "“", EndReplace = "”" },
            new AutoReplacementPair { Start = "a\\b", End = "c\nd", StartReplace = "e\tf", EndReplace = "g\rh" },
        ];
        var json = ext.SerializePairsJson();
        Assert.Contains("\"start\":", json);
        Assert.Contains("\\\\", json); // escaped backslash
        Assert.Contains("\\n", json);  // escaped newline
        Assert.Contains("\\t", json);
        Assert.Contains("\\r", json);

        ext.OnDocumentOpened(Ctx());
        ext.OnDocumentClosing(Ctx()); // no-ops, no throw
    }

    // ── DialogueCorrectionExtension ─────────────────────────────────
    [Fact]
    public void DialogueCorrection_DisabledConfig()
    {
        var ext = new DialogueCorrectionExtension();
        Assert.Equal("DialogueCorrection", ext.Name);
        Assert.Equal(55, ext.Priority);
        Assert.False(ext.Enabled);
        Assert.Equal("{\"enabled\":false}", ext.SerializeConfigJson());

        ext.Language = null!; // null -> en
        Assert.Equal("en", ext.Language);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("de-low", "de")]
    [InlineData("de-guillemet", "de")]
    [InlineData("fr-unsupported", "en")] // falls back to en rules
    public void DialogueCorrection_EnabledConfig_ByLanguage(string language, string expectedFamily)
    {
        var ext = new DialogueCorrectionExtension { Enabled = true, Language = language };
        var json = ext.SerializeConfigJson();
        Assert.Contains("\"enabled\":true", json);
        Assert.Contains($"\"ruleFamily\":\"{expectedFamily}\"", json);
        Assert.Contains("\"speechVerbs\":[", json);
        ext.OnDocumentOpened(Ctx());
        ext.OnDocumentClosing(Ctx());
    }
}

namespace Novalist.Backend;

internal static class ReadOnlyFileCachePolicy
{
    // Add methods only after auditing their full call path. Nested writes and
    // extension callbacks must explicitly enter UseFreshReads before running.
    private static readonly HashSet<string> Methods = new(StringComparer.Ordinal)
    {
        "scenes/read",
        "scenes/getAnnotations",
        "spell/dictionary",
        "entities/list",
        "entities/get",
        "entities/history",
        "entities/groups",
        "entities/customProps",
        "entities/getMatchSettings",
        "entities/getAiPolicy",
        "entities/getReaderPolicy",
        "entities/relationshipSuggestions",
        "entities/getStateOverrides",
        "entities/resolveState",
        "attachments/list",
        "arcs/get",
        "arcs/all",
        "context/analyze",
        "dashboard/get",
        "dashboard/overview",
        "dashboard/echoPhrases",
        "analytics/book",
        "analytics/castAbsence",
        "analytics/tension",
        "analytics/sceneFieldCurve"
    };

    internal static bool AllowsCachedReads(string? method) => method != null && Methods.Contains(method);
}

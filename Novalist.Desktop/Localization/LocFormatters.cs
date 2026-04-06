using Novalist.Core.Utilities;

namespace Novalist.Desktop.Localization;

internal static class LocFormatters
{
    public static string ReadingTime(int minutes) => minutes switch
    {
        < 1 => Loc.T("time.lessThanMinute"),
        < 60 => Loc.T("time.minutes", minutes),
        _ when minutes % 60 == 0 => Loc.T("time.hours", minutes / 60),
        _ => Loc.T("time.hoursMinutes", minutes / 60, minutes % 60)
    };

    public static string ReadabilityLevel(ReadabilityLevel level) => level switch
    {
        Novalist.Core.Utilities.ReadabilityLevel.VeryEasy => Loc.T("readability.veryEasy"),
        Novalist.Core.Utilities.ReadabilityLevel.Easy => Loc.T("readability.easy"),
        Novalist.Core.Utilities.ReadabilityLevel.Moderate => Loc.T("readability.moderate"),
        Novalist.Core.Utilities.ReadabilityLevel.Difficult => Loc.T("readability.difficult"),
        _ => Loc.T("readability.veryDifficult")
    };
}

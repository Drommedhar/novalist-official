namespace Novalist.Core.Services;

/// <summary>What a look at a stored project folder actually established.</summary>
public enum ProjectPresence
{
    /// <summary>The project is there and can be opened.</summary>
    Present,

    /// <summary>
    /// The project is gone, and we can say so: the folder it lived in is
    /// readable, and the project is not in it.
    /// </summary>
    Absent,

    /// <summary>
    /// Nothing could be established. The folder may be perfectly intact behind a
    /// grant that is not active, on a volume that is not mounted, or in a
    /// container the app has just been moved out of. Not a reason to forget it.
    /// </summary>
    Unknown
}

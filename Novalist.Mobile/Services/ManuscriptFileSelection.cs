namespace Novalist.Mobile.Services;

public static class ManuscriptFileSelection
{
    public static void RequireStandalone(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".scrivx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".scriv", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Scrivener requires its complete project folder. Android folder import is not available yet; export a DOCX or RTF manuscript from Scrivener first.");
    }
}

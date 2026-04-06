using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes custom export formats to the Export view.
/// </summary>
public interface IExportFormatContributor
{
    /// <summary>Returns custom export format descriptors.</summary>
    IReadOnlyList<ExportFormatDescriptor> GetExportFormats();
}

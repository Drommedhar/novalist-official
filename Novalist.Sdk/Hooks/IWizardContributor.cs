using System.Collections.Generic;
using Novalist.Sdk.Models.Wizards;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Extension hook: contributes additional <see cref="WizardDefinition"/>s to
/// the host. Wizards appear in the command palette under "Run wizard…" and
/// can be triggered directly from extension UIs via
/// <c>IHostServices.RunWizardAsync</c>.
/// </summary>
public interface IWizardContributor
{
    IReadOnlyList<WizardDefinition> GetWizards();
}

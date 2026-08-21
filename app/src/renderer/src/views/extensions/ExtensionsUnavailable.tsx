import { useTranslation } from 'react-i18next'

/**
 * What stands in for the extension feature in the Mac App Store build.
 *
 * An extension is a .NET assembly the app downloads and runs, and it adds views,
 * commands and hooks once it is there. The App Store does not allow an app to
 * bring in code that changes what it does after review, and the sandbox that
 * build runs under does not grant the entitlements to load one anyway - so the
 * feature is absent there rather than broken, and this says so in the two places
 * somebody would go looking for it: the Extensions view and its Settings card.
 *
 * It explains the edition it is, not the edition it is not: no link out, no
 * mention of a store or a price. A writer who needs extensions learns that the
 * directly-downloaded build has them, and that is the whole of it.
 */
export function ExtensionsUnavailable(): React.JSX.Element {
  const { t } = useTranslation()

  return (
    <>
      <p className="settings-hint">{t('extensions.unavailable.why')}</p>
      <p className="settings-hint">{t('extensions.unavailable.where')}</p>
    </>
  )
}

/**
 * The same explanation as a whole view, for the Extensions entry in Go and the
 * command palette. Those stay where they are rather than disappearing: a writer
 * who goes looking for extensions should find out what happened to them, and a
 * menu entry that is simply missing explains nothing.
 *
 * Rendered instead of ExtensionsView, not inside it, so none of the view's
 * loading effects run - no discovery call, no gallery request.
 */
export function ExtensionsUnavailableView(): React.JSX.Element {
  const { t } = useTranslation()

  return (
    <div className="dashboard extensions-view">
      <div className="extensions-header">
        <h1 className="dashboard-title">{t('extensions.title')}</h1>
      </div>
      <section className="dashboard-card">
        <div className="dashboard-card-title">{t('extensions.unavailable.title')}</div>
        <ExtensionsUnavailable />
      </section>
    </div>
  )
}

/** Whether this build has the extension feature at all. False only on MAS. */
export function extensionsAvailable(): boolean {
  return window.novalist.isMas !== true
}

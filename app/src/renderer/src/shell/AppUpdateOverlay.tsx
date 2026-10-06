import { UpdateDialog } from './UpdateDialog'
import type { AppUpdateState } from './appUpdateState'
import { runUpdateCheck, installAppUpdate, downloadAppUpdate, updateExtension } from './appUpdateActions'

export function AppUpdateOverlay({ updates }: { updates: AppUpdateState }): React.JSX.Element {
  const { appUpdate, appVersion, extUpdates, updatingExtId, updateProgress, downloading, installing, ready, manualInstall, checking, updateErrorMessage, setUpdateOpen, updateOpen } = updates
  return <>
      {updateOpen && (
        <UpdateDialog
          appUpdate={appUpdate}
          currentVersion={appVersion}
          extUpdates={extUpdates}
          updatingExtId={updatingExtId}
          progress={updateProgress}
          downloading={downloading}
          installing={installing}
          ready={ready}
          manualInstall={manualInstall}
          checking={checking}
          onCheck={() => void runUpdateCheck(updates, true)}
          onInstall={() => void installAppUpdate(updates)}
          error={updateErrorMessage}
          onDownload={() => void downloadAppUpdate(updates)}
          onUpdateExt={(u) => void updateExtension(updates, u)}
          onClose={() => {
            setUpdateOpen(false)
          }}
        />
      )}
  </>
}

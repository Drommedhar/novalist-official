import { TargetsPanel } from '../dashboard/TargetsCard'
import { type SettingsBodyContext } from './settingsBodyContext'
import { SettingInput } from './SettingsInputs'

export function writingGoalsSettingsBody(context: SettingsBodyContext): React.ReactNode {
  const { t, view, project, updateProjectMeta } = context
  return (
    <>
      <div className="settings-desc">{t('settings.goalsDesc')}</div>
      {view.hasProject && project ? (
        <>
          {projectMetadataSettings(context, project)}

          <label className="inspector-label" htmlFor="set-daily-goal">
            {t('settings.dailyWordGoal')}
          </label>
          <SettingInput
            id="set-daily-goal"
            value={String(project.dailyGoal)}
            onCommit={(v) => void updateProjectMeta({ dailyGoal: Number(v) || 0 })}
          />
          <div className="settings-hint">{t('settings.dailyWordGoalDesc')}</div>

          {/* Longer horizons. Blank or zero turns one off, which is what
                  every project starts at - nobody is given a weekly budget
                  they did not ask for. */}
          <label className="inspector-label" htmlFor="set-weekly-goal">
            {t('settings.weeklyWordGoal')}
          </label>
          <SettingInput
            id="set-weekly-goal"
            value={String(project.weeklyGoal || '')}
            onCommit={(v) => void updateProjectMeta({ weeklyGoal: Number(v) || 0 })}
          />
          <div className="settings-hint">{t('settings.weeklyWordGoalDesc')}</div>

          <label className="inspector-label" htmlFor="set-monthly-goal">
            {t('settings.monthlyWordGoal')}
          </label>
          <SettingInput
            id="set-monthly-goal"
            value={String(project.monthlyGoal || '')}
            onCommit={(v) => void updateProjectMeta({ monthlyGoal: Number(v) || 0 })}
          />
          <div className="settings-hint">{t('settings.monthlyWordGoalDesc')}</div>

          {/* Per project: a trade paperback, a mass-market and a large-print
                  edition are three different answers, and a writer working on
                  two of them at once needs two. */}
          <label className="inspector-label" htmlFor="set-words-per-page">
            {t('settings.wordsPerPage')}
          </label>
          <SettingInput
            id="set-words-per-page"
            value={String(project.wordsPerPage)}
            onCommit={(v) => void updateProjectMeta({ wordsPerPage: Number(v) || 0 })}
          />
          <div className="settings-hint">{t('settings.wordsPerPageDesc')}</div>

          <label className="inspector-label" htmlFor="set-project-goal">
            {t('settings.projectWordGoal')}
          </label>
          <SettingInput
            id="set-project-goal"
            value={String(project.projectGoal)}
            onCommit={(v) => void updateProjectMeta({ projectGoal: Number(v) || 0 })}
          />
          <div className="settings-hint">{t('settings.projectWordGoalDesc')}</div>

          {/* Per-act, per-chapter and per-scene targets. The same panel the
                  Dashboard shows, because this is where writers look for it. */}
          <label className="inspector-label">{t('targets.dashboardTitle')}</label>
          <TargetsPanel />
        </>
      ) : (
        <div className="settings-hint">{t('settings.scopeProjectHint')}</div>
      )}
    </>
  )
}

function projectMetadataSettings(context: SettingsBodyContext, project: NonNullable<SettingsBodyContext['project']>): React.ReactNode {
  const { t, updateProjectMeta } = context
  return <><label className="inspector-label" htmlFor="set-deadline">
    {t('settings.projectDeadline')}
  </label>
    <input
      id="set-deadline"
      className="dialog-input"
      type="date"
      value={project.deadline ?? ''}
      onChange={(e) => void updateProjectMeta({ deadline: e.target.value })}
    />
    <label className="inspector-label" htmlFor="set-author">
      {t('settings.projectAuthor')}
    </label>
    <SettingInput
      id="set-author"
      value={project.author}
      onCommit={(v) => void updateProjectMeta({ author: v })}
    /></>
}

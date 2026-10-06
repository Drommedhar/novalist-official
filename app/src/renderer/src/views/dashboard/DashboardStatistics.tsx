import { BookAnalyticsCard } from './BookAnalyticsCard'
import { type DashboardDto, type BookDashboardState } from './dashboardModel'

const STATUS_SUMMARY: { key: keyof DashboardDto; label: string }[] = [
  { key: 'outlineCount', label: 'dashboard.statusOutline' },
  { key: 'firstDraftCount', label: 'dashboard.statusFirstDraft' },
  { key: 'revisedCount', label: 'dashboard.statusRevised' },
  { key: 'editedCount', label: 'dashboard.statusEdited' },
  { key: 'finalCount', label: 'dashboard.statusFinal' }
]

export function DashboardWordStatistics({ data, t, statusTotal }: { data: DashboardDto; t: BookDashboardState['t']; statusTotal: BookDashboardState['statusTotal'] }): React.JSX.Element {
  return <>
      <div className="dashboard-card">
        <div className="dashboard-avg-grid">
          <div>
            <div className="dashboard-avg-value">{data.averageChapterWords.toLocaleString()}</div>
            <div className="dashboard-avg-label">{t('dashboard.avgPerChapter')}</div>
          </div>
          <div>
            <div className="dashboard-avg-value">{data.readingTimeMinutes} min</div>
            <div className="dashboard-avg-label">{t('dashboard.estReadingTime')}</div>
          </div>
        </div>
      </div>

      {data.statusBreakdown.length > 0 && (
        <div className="dashboard-card">
          <div className="dashboard-card-title">{t('dashboard.progressBreakdown')}</div>
          {data.statusBreakdown.map((s) => (
            <div key={s.status} className="dashboard-status-row">
              <span className="dashboard-status-dot" data-status={s.status} />
              <span className="dashboard-status-name">{t(`dashboard.status${s.status}`)}</span>
              <div className="dashboard-bar-track dashboard-status-track">
                <div
                  className="dashboard-bar-fill"
                  style={{ width: `${Math.round((s.count / statusTotal) * 100)}%` }}
                />
              </div>
              <span className="dashboard-status-count">
                {s.count} - {s.wordCount.toLocaleString()} {t('shell.words')}
              </span>
            </div>
          ))}
          <div className="dashboard-status-summary">
            {STATUS_SUMMARY.map((s) => (
              <div key={s.key}>
                <div className="dashboard-summary-count">{data[s.key] as number}</div>
                <div className="dashboard-summary-count-label">{t(s.label)}</div>
              </div>
            ))}
          </div>
        </div>
      )}
  </>
}

export function DashboardStoryProgress({ stageBreakdown, t, stageTotal, data }: { stageBreakdown: BookDashboardState['stageBreakdown']; t: BookDashboardState['t']; stageTotal: BookDashboardState['stageTotal']; data: DashboardDto }): React.JSX.Element {
  return <>
      {/* Scene stages, which the chapter breakdown above cannot express: a
          chapter mid-revision holds scenes at four different stages. */}
      {stageBreakdown.length > 0 && (
        <div className="dashboard-card">
          <div className="dashboard-card-title">{t('stages.title')}</div>
          {stageBreakdown.map((row) => (
            <div key={row.key || 'unset'} className="dashboard-status-row">
              <span className="dashboard-status-dot" style={{ background: row.color }} />
              <span className="dashboard-status-name">{row.label || t('stages.untriaged')}</span>
              <div className="dashboard-bar-track dashboard-status-track">
                <div
                  className="dashboard-bar-fill"
                  style={{ width: `${Math.round((row.sceneCount / stageTotal) * 100)}%` }}
                />
              </div>
              <span className="dashboard-status-count">
                {row.sceneCount} - {row.wordCount.toLocaleString()} {t('shell.words')}
              </span>
            </div>
          ))}
        </div>
      )}

      {/* Where things sit across the whole book rather than one scene at a time. */}
      <BookAnalyticsCard />

      {data.chapterPacing.length > 0 && (
        <div className="dashboard-card">
          <div className="dashboard-card-title">{t('dashboard.pacingAnalysis')}</div>
          <div className="dashboard-pacing-summary">
            <div>
              <div className="dashboard-summary-value">
                {data.longestChapterWords.toLocaleString()}
              </div>
              <div className="dashboard-summary-label">{t('dashboard.longestChapter')}</div>
            </div>
            <div>
              <div className="dashboard-summary-value">
                {data.shortestChapterWords.toLocaleString()}
              </div>
              <div className="dashboard-summary-label">{t('dashboard.shortestChapter')}</div>
            </div>
            <div>
              <div className="dashboard-summary-value">
                {Math.round(data.averageSceneWords).toLocaleString()}
              </div>
              <div className="dashboard-summary-label">{t('dashboard.avgSceneWords')}</div>
            </div>
          </div>
          {data.chapterPacing.map((c) => (
            <div key={c.title} className="dashboard-pacing-row">
              <span className="dashboard-pacing-title">{c.title}</span>
              <div className="dashboard-bar-track dashboard-status-track">
                <div
                  className="dashboard-bar-fill"
                  style={{
                    width: `${Math.round((c.words / Math.max(1, data.maxChapterWords)) * 100)}%`
                  }}
                />
              </div>
              <span className="dashboard-status-count">{c.words.toLocaleString()}</span>
            </div>
          ))}
        </div>
      )}

      {data.echoPhrases.length > 0 && (
        <div className="dashboard-card">
          <div className="dashboard-card-title">{t('dashboard.echoFinder')}</div>
          <div className="dashboard-echo-desc">{t('dashboard.echoDescription')}</div>
          <div className="dashboard-echoes">
            {data.echoPhrases.map((e) => (
              <span key={e.phrase} className="dashboard-echo">
                {e.phrase} <b>{e.count}</b>
              </span>
            ))}
          </div>
        </div>
      )}
  </>
}

import { BookAnalyticsCard } from './BookAnalyticsCard'
import { type DashboardDto, type BookDashboardState } from './dashboardModel'

export function DashboardWordStatistics({ data, t }: { data: DashboardDto; t: BookDashboardState['t'] }): React.JSX.Element {
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

  </>
}

export function DashboardStoryProgress({ stageBreakdown, t, stageTotal, data }: { stageBreakdown: BookDashboardState['stageBreakdown']; t: BookDashboardState['t']; stageTotal: BookDashboardState['stageTotal']; data: DashboardDto }): React.JSX.Element {
  return <>
      {/* Writing progress belongs to individual scenes. */}
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

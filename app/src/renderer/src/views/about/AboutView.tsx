import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import changelogSource from 'virtual:novalist-changelog'
import { useShellStore } from '../../stores/shellStore'
import './about.css'

/**
 * About: the screen for the facts that had nowhere to live.
 *
 * The app's own version was nowhere at all, the core process's was hidden in a
 * status-bar tooltip, the changelog only existed on a web page, "Check for
 * updates" was a menu item and nothing else, and the licences of the three
 * typefaces and two runtimes Novalist ships were never shown to anyone -
 * which is an obligation rather than a nicety. All of it is here, once.
 */

const REPO = 'https://github.com/Drommedhar/novalist-official'
const ISSUES = `${REPO}/issues/new`

/**
 * What Novalist bundles that asks to be credited.
 *
 * Licences are stated, never guessed: each typeface ships its own LICENSE file
 * beside the fonts and each names the SIL Open Font License 1.1; Electron and
 * the .NET runtime the core process is published against are both MIT. Anything
 * whose terms could not be read from the thing itself would be listed as
 * shipping its own licence text rather than given one here.
 */
interface Attribution {
  name: string
  roleKey: string
  license: string
  notice: string
}

export const ATTRIBUTIONS: Attribution[] = [
  {
    name: 'Fraunces',
    roleKey: 'about.role.displayFace',
    license: 'SIL Open Font License 1.1',
    notice: 'Copyright 2020 The Fraunces Project Authors'
  },
  {
    name: 'Newsreader',
    roleKey: 'about.role.bodyFace',
    license: 'SIL Open Font License 1.1',
    notice: 'Copyright 2020 The Newsreader Project Authors'
  },
  {
    name: 'Courier Prime',
    roleKey: 'about.role.monoFace',
    license: 'SIL Open Font License 1.1',
    notice: 'Copyright 2015 The Courier Prime Project Authors'
  },
  {
    name: 'Electron',
    roleKey: 'about.role.desktopRuntime',
    license: 'MIT License',
    notice: 'Includes Chromium and Node.js, whose own licence texts ship with it'
  },
  {
    name: '.NET Runtime',
    roleKey: 'about.role.coreRuntime',
    license: 'MIT License',
    notice: 'Copyright the .NET Foundation and Contributors'
  }
]

/**
 * The changelog without its preamble.
 *
 * The file opens with a paragraph about Keep a Changelog, the tagging workflow
 * and where iOS releases live. That is written for whoever edits the file; a
 * writer opening About wants the releases.
 */
export function changelogBody(source: string): string {
  const at = source.search(/^## /m)
  return at < 0 ? source : source.slice(at)
}

/** A version out of the user-agent string, which is where the shell names itself. */
function agentVersion(product: string): string | null {
  return new RegExp(`${product}/([\\d.]+)`).exec(navigator.userAgent)?.[1] ?? null
}

interface DisplayFacts {
  zoomFactor: number
  scaleFactor: number
  windowBounds: { width: number; height: number }
  contentBounds: { width: number; height: number }
  workArea: { width: number; height: number }
}

/**
 * The support block, in the same content-free spirit the diagnostic log
 * follows: versions, sizes, and the names of things - never a project, a path,
 * or a line of prose. Written in English rather than the interface language
 * because it is pasted into a support thread rather than read on screen.
 */
export function systemInfoBlock(facts: {
  appVersion: string | null
  coreVersion: string | null
  platform: string
  electron: string | null
  chromium: string | null
  uiLanguage: string
  systemLocale: string
  display: DisplayFacts | null
}): string {
  const size = (box: { width: number; height: number }): string =>
    `${Math.round(box.width)} x ${Math.round(box.height)}`
  const lines = [
    `Novalist ${facts.appVersion ?? 'unknown'}`,
    `Core process ${facts.coreVersion ?? 'not connected'}`,
    `Platform ${facts.platform}`,
    `Electron ${facts.electron ?? 'unknown'}`,
    `Chromium ${facts.chromium ?? 'unknown'}`,
    `Interface language ${facts.uiLanguage}`,
    `System locale ${facts.systemLocale}`
  ]
  if (facts.display) {
    lines.push(
      `Interface scale ${Math.round(facts.display.zoomFactor * 100)}%`,
      `Display scale ${Math.round(facts.display.scaleFactor * 100)}%`,
      `Window ${size(facts.display.windowBounds)}`,
      `Content ${size(facts.display.contentBounds)}`,
      `Work area ${size(facts.display.workArea)}`
    )
  }
  return lines.join('\n')
}

export function AboutView(): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const backendVersion = useShellStore((s) => s.backendVersion)
  const [appVersion, setAppVersion] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const changelog = useMemo(() => changelogBody(changelogSource), [])

  useEffect(() => {
    let live = true
    void window.novalist.appVersion?.().then((version) => {
      if (live) setAppVersion(version)
    })
    return () => {
      live = false
    }
  }, [])

  // The copied confirmation is a moment, not a state to be dismissed.
  useEffect(() => {
    if (!copied) return
    const timer = window.setTimeout(() => setCopied(false), 4000)
    return () => window.clearTimeout(timer)
  }, [copied])

  const openLink = (event: React.MouseEvent, href: string): void => {
    event.preventDefault()
    void window.novalist.openExternal(href)
  }

  /**
   * The same check the Help menu runs.
   *
   * The updater lives in the shell around this view, and it already listens for
   * the message the menu bar sends through the preload - so About asks for the
   * check the way the menu does rather than growing a second copy of the flow.
   */
  const checkForUpdates = (): void => {
    window.postMessage({ novalist: 'menu-command', command: 'help:checkUpdates' }, '*')
  }

  const copySystemInfo = async (): Promise<void> => {
    const display = (await window.novalist.displayDiagnostics?.()) ?? null
    window.novalist.copyText(
      systemInfoBlock({
        appVersion,
        coreVersion: backendVersion,
        platform: String(window.novalist.platform),
        electron: agentVersion('Electron'),
        chromium: agentVersion('Chrome'),
        uiLanguage: i18n.language,
        systemLocale: navigator.language,
        display
      })
    )
    setCopied(true)
  }

  return (
    <div className="about-view">
      <header className="about-head">
        <h1 className="about-title">{t('about.title')}</h1>
        <dl className="about-versions" data-testid="about-versions">
          <div>
            <dt>{t('about.appVersion')}</dt>
            <dd data-testid="about-app-version">{appVersion ?? t('about.unknown')}</dd>
          </div>
          <div>
            <dt>{t('about.coreVersion')}</dt>
            <dd data-testid="about-core-version">{backendVersion ?? t('about.unknown')}</dd>
          </div>
        </dl>
        <p className="about-links">
          <a href={REPO} onClick={(e) => openLink(e, REPO)}>
            {t('about.website')}
          </a>
          <a href={ISSUES} onClick={(e) => openLink(e, ISSUES)}>
            {t('about.reportIssue')}
          </a>
        </p>
        <div className="about-actions">
          {/* The Mac App Store delivers its own updates and forbids self-update,
              so the check is absent there rather than present and failing. */}
          {!window.novalist.isMas && (
            <button type="button" className="dialog-button" onClick={checkForUpdates}>
              {t('about.checkUpdates')}
            </button>
          )}
          <button
            type="button"
            className="dialog-button"
            data-testid="about-copy-system-info"
            onClick={() => void copySystemInfo()}
          >
            {t('about.copySystemInfo')}
          </button>
          <span className="about-copied" role="status">
            {copied ? t('about.copied') : ''}
          </span>
        </div>
        <p className="about-hint">{t('about.copySystemInfoDesc')}</p>
      </header>

      <section className="about-section">
        <h2 className="about-section-title">{t('about.whatsNew')}</h2>
        <div className="about-changelog" data-testid="about-changelog">
          <Markdown
            remarkPlugins={[remarkGfm]}
            components={{
              a: ({ href, children, ...rest }) => (
                <a
                  {...rest}
                  href={href}
                  onClick={(e) => {
                    e.preventDefault()
                    if (href && /^https?:/i.test(href)) void window.novalist.openExternal(href)
                  }}
                >
                  {children}
                </a>
              )
            }}
          >
            {changelog}
          </Markdown>
        </div>
      </section>

      <section className="about-section">
        <h2 className="about-section-title">{t('about.licenses')}</h2>
        <p className="about-hint">{t('about.licensesIntro')}</p>
        <ul className="about-licenses" data-testid="about-licenses">
          {ATTRIBUTIONS.map((entry) => (
            <li key={entry.name} className="about-license">
              <span className="about-license-name">{entry.name}</span>
              <span className="about-license-role">{t(entry.roleKey)}</span>
              <span className="about-license-terms">{entry.license}</span>
              <span className="about-license-notice">{entry.notice}</span>
            </li>
          ))}
        </ul>
        <p className="about-hint">{t('about.licensesNote')}</p>
      </section>
    </div>
  )
}

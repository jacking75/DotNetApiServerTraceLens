import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, buildReport } from '../api/client'
import { translateError, useI18n } from '../i18n/I18nProvider'
import type { SelectionKind, ShareReport } from '../types/context'

interface ShareReportDialogProps {
  kind: SelectionKind
  id: string
  onClose: () => void
}

/**
 * 팀에 붙여넣을 수 있는 공유용 Markdown 보고서를 만든다.
 * 절대 경로와 소스 본문은 서버(`ShareReportBuilder`)가 항상 제외한다 — 여기서 켤 수 있는 것은 상대 경로와 저장소 이름뿐이다.
 */
export function ShareReportDialog({ kind, id, onClose }: ShareReportDialogProps) {
  const { t } = useI18n()
  const [includePaths, setIncludePaths] = useState(true)
  const [includeStorageNames, setIncludeStorageNames] = useState(true)
  const [report, setReport] = useState<ShareReport | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const closeRef = useRef<HTMLButtonElement>(null)
  const previouslyFocused = useRef<Element | null>(null)

  useEffect(() => {
    previouslyFocused.current = document.activeElement
    closeRef.current?.focus()
    return () => {
      if (previouslyFocused.current instanceof HTMLElement) previouslyFocused.current.focus()
    }
  }, [])

  const load = useCallback(async () => {
    setBusy(true)
    setMessage(null)
    try {
      setReport(await buildReport({ kind, id, includePaths, includeStorageNames }))
    } catch (reason) {
      setReport(null)
      setMessage(reason instanceof ApiError ? translateError(t, reason.code, reason.message) : t('share.failed'))
    } finally {
      setBusy(false)
    }
  }, [kind, id, includePaths, includeStorageNames, t])

  useEffect(() => { void load() }, [load])

  const copy = async () => {
    if (!report) return
    try {
      await navigator.clipboard.writeText(report.markdown)
      setMessage(t('ask.copied'))
    } catch {
      setMessage(t('ask.copyFailed'))
    }
  }

  const save = () => {
    if (!report) return
    const blob = new Blob([report.markdown], { type: 'text/markdown;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${report.title.replace(/[\\/:*?"<>|]/g, '_')}.md`
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div
      className="modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-label={t('share.title')}
      onKeyDown={(event) => { if (event.key === 'Escape') onClose() }}
    >
      <div className="modal share-modal">
        <header>
          <span>{t('share.title')}</span>
          <button type="button" className="link-button" ref={closeRef} onClick={onClose}>{t('ask.close')}</button>
        </header>
        <div className="hint">{t('share.privacy')}</div>
        <div className="ask-options">
          <label className="checkbox">
            <input
              type="checkbox"
              checked={includePaths}
              onChange={(event) => setIncludePaths(event.target.checked)}
            />
            {t('share.includePaths')}
          </label>
          <label className="checkbox">
            <input
              type="checkbox"
              checked={includeStorageNames}
              onChange={(event) => setIncludeStorageNames(event.target.checked)}
            />
            {t('share.includeStorageNames')}
          </label>
        </div>
        {report && report.included.length > 0 && (
          <ul className="context-files">
            {report.included.map((item) => <li key={item}>{item}</li>)}
          </ul>
        )}
        <pre className="share-preview">{busy ? t('app.analyzing') : report?.markdown ?? ''}</pre>
        <div className="ask-actions">
          <button type="button" className="primary" onClick={() => void copy()} disabled={!report}>
            {t('ask.copy')}
          </button>
          <button type="button" onClick={save} disabled={!report}>{t('share.save')}</button>
          {message && <div className="copy-message">{message}</div>}
        </div>
      </div>
    </div>
  )
}

import { useCallback, useEffect, useState } from 'react'
import { ApiError, compareSnapshots, deleteSnapshot, listSnapshots, saveSnapshot } from '../api/client'
import { translateError, useI18n } from '../i18n/I18nProvider'
import type { DiffEntry, SnapshotDiff, SnapshotEntry } from '../types/context'
import type { TranslationKey } from '../i18n/ko'

interface SnapshotDiffPanelProps {
  snapshotId: string
  analyzing: boolean
  /** 비교 전에 사용자가 명시적으로 실행하는 재분석. 자동으로 부르지 않는다. */
  onReanalyze: () => Promise<boolean>
  onClose: () => void
}

const sections: ReadonlyArray<{ key: keyof Pick<SnapshotDiff, 'endpoints' | 'methods' | 'edges' | 'dataAccess' | 'findings' | 'renameCandidates'>; labelKey: TranslationKey }> = [
  { key: 'endpoints', labelKey: 'diff.endpoints' },
  { key: 'methods', labelKey: 'diff.methods' },
  { key: 'edges', labelKey: 'diff.edges' },
  { key: 'dataAccess', labelKey: 'diff.dataAccess' },
  { key: 'findings', labelKey: 'diff.findings' },
  { key: 'renameCandidates', labelKey: 'diff.renames' },
]

/**
 * "변경 전으로 저장 → 외부 AI로 수정 → 재분석하고 비교" 흐름을 담는다.
 * 재분석은 이 패널에서도 사용자가 누를 때만 실행된다.
 */
export function SnapshotDiffPanel({ snapshotId, analyzing, onReanalyze, onClose }: SnapshotDiffPanelProps) {
  const { t, formatDateTime } = useI18n()
  const [entries, setEntries] = useState<SnapshotEntry[]>([])
  const [baselineId, setBaselineId] = useState<string>('')
  const [diff, setDiff] = useState<SnapshotDiff | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  /** 서버가 알려 준 "지워도 될 기준" 목록. 비어 있으면 정리 버튼을 띄우지 않는다. */
  const [pruneCandidates, setPruneCandidates] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)

  const describe = useCallback((reason: unknown, fallbackKey: TranslationKey) =>
    reason instanceof ApiError ? translateError(t, reason.code, reason.message) : t(fallbackKey), [t])

  const refresh = useCallback(async () => {
    try {
      const response = await listSnapshots()
      setEntries(response.entries)
      setBaselineId((current) => current || response.entries[0]?.snapshotId || '')
    } catch (reason) {
      setError(describe(reason, 'diff.failed'))
    }
  }, [describe])

  useEffect(() => { void refresh() }, [refresh])

  const save = async () => {
    setBusy(true)
    setError(null)
    try {
      const response = await saveSnapshot(t('diff.baseline'))
      setBaselineId(response.entry.snapshotId)
      setMessage(t('diff.saved', { id: response.entry.snapshotId }) + (response.notice ? ` ${response.notice}` : ''))
      setPruneCandidates(response.pruneCandidates ?? [])
      await refresh()
    } catch (reason) {
      setError(describe(reason, 'diff.failed'))
    } finally {
      setBusy(false)
    }
  }

  const compare = async () => {
    if (!baselineId) return
    setBusy(true)
    setError(null)
    setMessage(null)
    setDiff(null)
    try {
      if (!await onReanalyze()) {
        setError(t('diff.analysisIncomplete'))
        return
      }
      setDiff(await compareSnapshots(baselineId))
    } catch (reason) {
      setError(describe(reason, 'diff.failed'))
    } finally {
      setBusy(false)
    }
  }

  /** 서버가 알려 준 "지워도 될 후보"를 한 번에 지운다. 자동 삭제는 하지 않고 이 버튼으로만 지운다. */
  const prune = async () => {
    setBusy(true)
    try {
      for (const id of pruneCandidates) {
        await deleteSnapshot(id)
        if (baselineId === id) setBaselineId('')
      }

      setMessage(t('diff.pruned', { count: pruneCandidates.length }))
      setPruneCandidates([])
      await refresh()
    } catch (reason) {
      setError(describe(reason, 'diff.failed'))
    } finally {
      setBusy(false)
    }
  }

  const remove = async (id: string) => {
    setBusy(true)
    try {
      await deleteSnapshot(id)
      if (baselineId === id) setBaselineId('')
      setDiff(null)
      await refresh()
    } catch (reason) {
      setError(describe(reason, 'diff.failed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="diff-panel" aria-label={t('diff.title')}>
      <header>
        <span>{t('diff.title')}</span>
        <button type="button" className="link-button" onClick={onClose}>{t('ask.close')}</button>
      </header>

      <div className="diff-actions">
        <button type="button" onClick={() => void save()} disabled={busy || analyzing || !snapshotId}>
          {busy ? t('diff.saving') : t('diff.saveBaseline')}
        </button>
        <label>
          {t('diff.baseline')}
          <select value={baselineId} onChange={(event) => setBaselineId(event.target.value)} disabled={entries.length === 0}>
            {entries.map((entry) => (
              <option key={entry.snapshotId} value={entry.snapshotId}>
                {entry.label} · {formatDateTime(entry.savedAt)}
              </option>
            ))}
          </select>
        </label>
        <button type="button" className="primary" onClick={() => void compare()} disabled={busy || analyzing || !baselineId}>
          {busy || analyzing ? t('diff.comparing') : t('diff.compare')}
        </button>
        {baselineId && (
          <button type="button" className="link-button" onClick={() => void remove(baselineId)} disabled={busy}>
            {t('diff.delete')}
          </button>
        )}
      </div>

      {entries.length === 0 && <div className="panel-message tight">{t('diff.none')}</div>}
      {message && <div className="hint">{message}</div>}
      {pruneCandidates.length > 0 && (
        <button type="button" className="link-button" onClick={() => void prune()} disabled={busy}>
          {t('diff.prune', { count: pruneCandidates.length })}
        </button>
      )}
      {error && <div className="panel-message tight">{error}</div>}

      {diff && (
        <div className="diff-body">
          <div className={diff.preconditions.ok ? 'hint' : 'hint warn'}>
            {diff.preconditions.ok ? t('diff.preconditionsOk') : t('diff.preconditionsBad')}
            {diff.preconditions.differences.map((item) => <div key={item}>· {item}</div>)}
            {diff.preconditions.notes.map((item) => <div key={item}>· {item}</div>)}
          </div>

          {sections.map((section) => (
            <details key={section.key} open={diff[section.key].length > 0 && section.key !== 'edges'}>
              <summary>
                {t(section.labelKey)} ({diff[section.key].length})
              </summary>
              {diff[section.key].length === 0
                ? <div className="hint">{t('diff.empty')}</div>
                : <ul className="diff-list">{diff[section.key].map(renderEntry)}</ul>}
            </details>
          ))}

          <details>
            <summary>{t('context.limitations')}</summary>
            <ul className="context-limitations">
              {diff.limitations.map((item) => <li key={item}>{item}</li>)}
            </ul>
          </details>
          <div className="hint warn">{t('diff.noDiffNote')}</div>
        </div>
      )}
    </section>
  )

  function renderEntry(entry: DiffEntry) {
    const kindLabel: Record<DiffEntry['kind'], TranslationKey> = {
      Added: 'diff.added',
      Removed: 'diff.removed',
      Changed: 'diff.changed',
      Unchanged: 'diff.empty',
      NotComparable: 'diff.notComparable',
    }
    return (
      <li key={entry.id} className={`diff-${entry.kind.toLowerCase()}`}>
        <span className="chip small">{t(kindLabel[entry.kind])}</span>
        <b>{entry.label}</b>
        {entry.detail && <div className="hint">{entry.detail}</div>}
        {entry.file && <code>{entry.file}:{entry.line}</code>}
      </li>
    )
  }
}

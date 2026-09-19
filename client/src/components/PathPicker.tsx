import { useCallback, useEffect, useRef, useState } from 'react'
import { useModal } from '../hooks/useModal'
import { browsePath } from '../api/client'
import { useI18n } from '../i18n/I18nProvider'
import type { BrowseEntry, BrowseResponse } from '../types/graph'

/** 파일 종류를 한눈에 구분하기 위한 배지 문구. 코드 표기라 번역하지 않는다. */
const kindBadge: Record<BrowseEntry['kind'], string> = {
  Directory: '',
  Solution: 'SLN',
  Project: 'CSPROJ',
  Unsupported: '?',
}

interface PathPickerProps {
  /** 대화상자를 열 때 처음 보여줄 경로. 비어 있으면 드라이브 목록부터 시작한다. */
  initialPath: string
  /**
   * 무엇을 고르는지. `target`은 .sln/.csproj 파일, `directory`는 지금 열어 둔 폴더 자체다.
   * 가져온 분석의 원본 루트를 연결할 때 `directory`를 쓴다.
   */
  mode?: 'target' | 'directory'
  onSelect: (path: string) => void
  onClose: () => void
}

/**
 * 분석 대상(.sln / .csproj)이나 폴더를 고르는 대화상자.
 * 브라우저 파일 대화상자는 절대 경로를 알려주지 않으므로 서버의 `/api/browse`로 디렉터리를 탐색한다.
 * 열리면 닫기 버튼에 포커스를 두고, Escape로 닫고, 닫힐 때 이전 포커스를 되돌린다.
 */
export function PathPicker({ initialPath, mode = 'target', onSelect, onClose }: PathPickerProps) {
  const { t } = useI18n()
  const [listing, setListing] = useState<BrowseResponse | null>(null)
  const [selected, setSelected] = useState<BrowseEntry | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const closeRef = useRef<HTMLButtonElement>(null)
  const modalRef = useModal<HTMLDivElement>(onClose, closeRef)

  const load = useCallback(async (path?: string) => {
    setBusy(true)
    setError(null)
    try {
      const next = await browsePath(path)
      setListing(next)
      setSelected(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : t('picker.openFailed'))
    } finally {
      setBusy(false)
    }
  }, [t])

  useEffect(() => {
    void load(initialPath.trim() || undefined)
  }, [initialPath, load])

  const openEntry = (entry: BrowseEntry) => {
    if (entry.kind === 'Directory') {
      void load(entry.path)
      return
    }

    // 지원하지 않는 형식은 고를 수 없다. 목록에서 빼면 .slnx만 있는 폴더가 빈 폴더로 보인다.
    if (entry.kind === 'Unsupported') return

    setSelected(entry)
  }

  const entries = listing?.entries ?? []
  const pickDirectory = mode === 'directory'
  const chosen = pickDirectory ? listing?.path ?? null : selected?.path ?? null
  const title = pickDirectory ? t('picker.titleDirectory') : t('picker.title')

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <section
        className="path-picker"
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(event) => event.stopPropagation()}
      >
        <header>
          <span>{title}</span>
          <button
            type="button"
            className="picker-close"
            ref={closeRef}
            onClick={onClose}
            aria-label={t('picker.close')}
          >
            ✕
          </button>
        </header>

        <div className="picker-path">
          <button
            type="button"
            onClick={() => void load(listing?.parentPath ?? undefined)}
            disabled={busy || !listing?.path}
            title={t('picker.up')}
          >
            ↑ {t('picker.up')}
          </button>
          <span title={listing?.path ?? ''}>{listing?.path ?? t('picker.drives')}</span>
        </div>

        {error && <div className="panel-message error">{error}</div>}

        <div className="picker-list">
          {busy && <div className="panel-message">{t('picker.loading')}</div>}
          {!busy && entries.length === 0 && !error && (
            <div className="panel-message">{t('picker.empty')}</div>
          )}
          {!busy && entries.map((entry) => (
            <button
              type="button"
              key={entry.path}
              className={`picker-row ${entry.kind.toLowerCase()} ${selected?.path === entry.path ? 'selected' : ''}`}
              onClick={() => openEntry(entry)}
              onDoubleClick={() => entry.kind !== 'Directory' && entry.kind !== 'Unsupported' && onSelect(entry.path)}
              disabled={entry.kind === 'Unsupported'}
              title={entry.kind === 'Unsupported' ? `${entry.path}\n${entry.reason ?? ''}` : entry.path}
            >
              <span className="picker-icon">{entry.kind === 'Directory' ? '📁' : '📄'}</span>
              <span className="picker-name">{entry.name}</span>
              {kindBadge[entry.kind] && <span className="picker-badge">{kindBadge[entry.kind]}</span>}
              {entry.kind === 'Unsupported' && (
                <span className="picker-reason">{entry.reason || t('diagnostics.unsupportedFormat')}</span>
              )}
            </button>
          ))}
        </div>

        <footer>
          <span className="picker-selection" title={chosen ?? ''}>
            {chosen ?? (pickDirectory ? t('picker.hintDirectory') : t('picker.hint'))}
          </span>
          <button type="button" onClick={onClose}>{t('picker.cancel')}</button>
          <button
            type="button"
            className="primary"
            disabled={!chosen}
            onClick={() => chosen && onSelect(chosen)}
          >
            {t('picker.select')}
          </button>
        </footer>
      </section>
    </div>
  )
}

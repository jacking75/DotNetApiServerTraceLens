import { useCallback, useEffect, useState } from 'react'
import { browsePath } from '../api/client'
import type { BrowseEntry, BrowseResponse } from '../types/graph'

/** 파일 종류를 한눈에 구분하기 위한 배지 문구. */
const kindBadge: Record<BrowseEntry['kind'], string> = {
  Directory: '',
  Solution: 'SLN',
  Project: 'CSPROJ',
}

interface PathPickerProps {
  /** 대화상자를 열 때 처음 보여줄 경로. 비어 있으면 드라이브 목록부터 시작한다. */
  initialPath: string
  onSelect: (path: string) => void
  onClose: () => void
}

/**
 * 분석 대상(.sln / .csproj)을 고르는 대화상자.
 * 브라우저 파일 대화상자는 절대 경로를 알려주지 않으므로 서버의 `/api/browse`로 디렉터리를 탐색한다.
 */
export function PathPicker({ initialPath, onSelect, onClose }: PathPickerProps) {
  const [listing, setListing] = useState<BrowseResponse | null>(null)
  const [selected, setSelected] = useState<BrowseEntry | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async (path?: string) => {
    setBusy(true)
    setError(null)
    try {
      const next = await browsePath(path)
      setListing(next)
      setSelected(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '폴더를 열지 못했다.')
    } finally {
      setBusy(false)
    }
  }, [])

  useEffect(() => {
    void load(initialPath.trim() || undefined)
  }, [initialPath, load])

  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [onClose])

  const openEntry = (entry: BrowseEntry) => {
    if (entry.kind === 'Directory') {
      void load(entry.path)
      return
    }

    setSelected(entry)
  }

  const entries = listing?.entries ?? []

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <section
        className="path-picker"
        role="dialog"
        aria-modal="true"
        aria-label="분석 대상 선택"
        onClick={(event) => event.stopPropagation()}
      >
        <header>
          <span>분석 대상 선택</span>
          <button type="button" className="picker-close" onClick={onClose} aria-label="닫기">✕</button>
        </header>

        <div className="picker-path">
          <button
            type="button"
            onClick={() => void load(listing?.parentPath ?? undefined)}
            disabled={busy || !listing?.path}
            title="상위 폴더로"
          >
            ↑ 상위
          </button>
          <span title={listing?.path ?? ''}>{listing?.path ?? '드라이브'}</span>
        </div>

        {error && <div className="panel-message error">{error}</div>}

        <div className="picker-list">
          {busy && <div className="panel-message">폴더를 읽는 중이다.</div>}
          {!busy && entries.length === 0 && !error && (
            <div className="panel-message">이 폴더에는 하위 폴더나 .sln / .csproj 파일이 없다.</div>
          )}
          {!busy && entries.map((entry) => (
            <button
              type="button"
              key={entry.path}
              className={`picker-row ${entry.kind.toLowerCase()} ${selected?.path === entry.path ? 'selected' : ''}`}
              onClick={() => openEntry(entry)}
              onDoubleClick={() => entry.kind !== 'Directory' && onSelect(entry.path)}
              title={entry.path}
            >
              <span className="picker-icon">{entry.kind === 'Directory' ? '📁' : '📄'}</span>
              <span className="picker-name">{entry.name}</span>
              {kindBadge[entry.kind] && <span className="picker-badge">{kindBadge[entry.kind]}</span>}
            </button>
          ))}
        </div>

        <footer>
          <span className="picker-selection" title={selected?.path ?? ''}>
            {selected?.path ?? '.sln 또는 .csproj 파일을 고른다. 폴더는 눌러서 들어간다.'}
          </span>
          <button type="button" onClick={onClose}>취소</button>
          <button
            type="button"
            className="primary"
            disabled={!selected}
            onClick={() => selected && onSelect(selected.path)}
          >
            선택
          </button>
        </footer>
      </section>
    </div>
  )
}

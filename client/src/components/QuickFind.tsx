import { useEffect, useMemo, useRef, useState } from 'react'
import { resolveRecentHits, searchGraph, type SearchHit } from '../graph/search'
import { useModal } from '../hooks/useModal'
import { useI18n } from '../i18n/I18nProvider'
import type { Bookmark } from '../state/viewState'
import type { GraphDocument } from '../types/graph'

interface QuickFindProps {
  graph: GraphDocument | null
  /** 최근에 고른 항목(`kind:id`). 빈 입력일 때 보여 준다. */
  recent: string[]
  /** ★로 찍어 둔 자리. 빈 입력일 때 최근과 함께 보여 준다. */
  bookmarks: Bookmark[]
  onPickBookmark: (bookmark: Bookmark) => void
  onPick: (hit: SearchHit) => void
  onClose: () => void
}

/**
 * `Ctrl+K` 통합 찾기. 진입점·메서드·클래스·규칙·파일을 한 입력칸에서 찾는다.
 * 파일 트리 탐색기를 만들지 않고(설계 제약 1) 기존 탐색 축 안에서 검색만 넓히는 장치다.
 * 계산은 `graph/search.ts`의 순수 함수가 하고 여기서는 그리기만 한다.
 */
export function QuickFind({ graph, recent, bookmarks, onPickBookmark, onPick, onClose }: QuickFindProps) {
  const { t } = useI18n()
  const [query, setQuery] = useState('')
  /** 구현체를 펼친 결과 줄. 인터페이스 멤버를 고르면 어디로 갈지 먼저 고르게 한다. */
  const [expanded, setExpanded] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const resultsRef = useRef<HTMLDivElement>(null)
  const modalRef = useModal<HTMLDivElement>(onClose, inputRef)

  useEffect(() => { inputRef.current?.focus() }, [])

  const result = useMemo(() => searchGraph(graph, query), [graph, query])
  const flat = useMemo(
    () => [...result.entryPoints, ...result.methods, ...result.classes, ...result.rules, ...result.files],
    [result])

  useEffect(() => { setExpanded(null) }, [query])

  const groups: { key: string; label: string; hits: SearchHit[] }[] = [
    { key: 'entryPoints', label: t('panel.entryPoints'), hits: result.entryPoints },
    { key: 'methods', label: t('detail.methods'), hits: result.methods },
    { key: 'classes', label: t('detail.classes'), hits: result.classes },
    { key: 'rules', label: `${t('quickFind.kindRule')} · ${t('quickFind.kindFinding')}`, hits: result.rules },
    { key: 'files', label: t('quickFind.kindFile'), hits: result.files },
  ]

  const pick = (hit: SearchHit) => {
    // 구현체가 여럿이면 어디로 갈지 먼저 고르게 한다. 하나면 바로 간다.
    if ((hit.implementations?.length ?? 0) > 1) {
      setExpanded((current) => (current === hit.id ? null : hit.id))
      return
    }

    onPick(hit)
  }

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.nativeEvent.isComposing) return
    const inInput = event.target === inputRef.current
    const rows = [...(resultsRef.current?.querySelectorAll<HTMLButtonElement>('.find-row') ?? [])]
    const index = rows.indexOf(event.target as HTMLButtonElement)
    if (!inInput && index < 0) return
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp' ||
      (!inInput && (event.key === 'Home' || event.key === 'End'))) {
      if (rows.length === 0) return
      event.preventDefault()
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? rows.length - 1
        : inInput ? (event.key === 'ArrowDown' ? 0 : rows.length - 1)
          : (index + (event.key === 'ArrowDown' ? 1 : -1) + rows.length) % rows.length
      rows[next].focus()
      rows[next].scrollIntoView?.({ block: 'nearest' })
      return
    }

    if (inInput && event.key === 'Enter' && rows.length > 0) {
      event.preventDefault()
      rows[0].focus()
      rows[0].click()
    }
  }

  const recentHits = useMemo(() => resolveRecentHits(graph, recent), [graph, recent])

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal quick-find"
        role="dialog"
        aria-modal="true"
        aria-label={t('find.title')}
        ref={modalRef}
        onClick={(event) => event.stopPropagation()}
        onKeyDown={onKeyDown}
      >
        <div className="quick-find-heading">
          <h2>{t('find.title')}</h2>
          <button type="button" onClick={onClose} aria-label={t('picker.close')}>✕</button>
        </div>
        <input
          ref={inputRef}
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder={t('find.placeholder')}
          aria-label={t('find.title')}
          aria-describedby="quick-find-help"
        />
        <div className="quick-find-tools">
          <span id="quick-find-help" className="hint">{t('find.hint')}</span>
          {query.length > 0 && <button type="button" className="link-button" onClick={() => {
            setQuery('')
            inputRef.current?.focus()
          }}>{t('search.clear')}</button>}
        </div>
        {query.trim().length > 0 && <div className="hint" role="status">{t('find.count', { count: flat.length })}</div>}
        <div className="quick-find-results" ref={resultsRef}>
        {query.trim().length === 0 && (
          <>
            <div className="hint">{t('quickFind.filterHint')}</div>
            {bookmarks.length > 0 && (
              <>
                <div className="side-group">{t('bookmark.section')}</div>
                {bookmarks.slice(0, 10).map((bookmark) => (
                  <button
                    key={`${bookmark.selection.type}:${bookmark.selection.key}`}
                    type="button"
                    className="find-row"
                    onClick={() => onPickBookmark(bookmark)}
                  >
                    <span className="grow">{bookmark.label}</span>
                    <span className="hint">{t(`selection.${bookmark.selection.type}` as const)}</span>
                  </button>
                ))}
              </>
            )}
            {recentHits.length > 0 && (
              <>
                <div className="side-group">{t('quickFind.recent')}</div>
                {recentHits.map((item) => (
                  <button
                    key={`${item.kind}:${item.id}`}
                    type="button"
                    className="find-row"
                    onClick={() => onPick(item)}
                    title={`${item.label}\n${item.detail}`}
                  >
                    <span className="grow">{item.label}</span>
                    <span className="hint">{item.detail}</span>
                  </button>
                ))}
              </>
            )}
          </>
        )}
        {query.trim().length > 0 && flat.length === 0 && (
          <div className="panel-message tight">{t('find.empty')}</div>
        )}
        {groups.filter((group) => group.hits.length > 0).map((group) => (
          <div key={group.key}>
            <div className="side-group">{group.label}</div>
            {group.hits.map((hit) => (
              <div key={`${hit.kind}:${hit.id}:${hit.label}`}>
                <button
                  type="button"
                  className="find-row"
                  onClick={() => pick(hit)}
                  title={`${hit.label}\n${hit.detail}`}
                  aria-expanded={(hit.implementations?.length ?? 0) > 1 ? expanded === hit.id : undefined}
                >
                  <span className="grow">{hit.label}</span>
                  {(hit.implementations?.length ?? 0) > 0 && (
                    <span className="chip small">
                      {t('quickFind.implementations', { count: hit.implementations!.length })}
                    </span>
                  )}
                  <span className="hint">{hit.detail}</span>
                </button>
                {expanded === hit.id && hit.implementations?.map((item) => (
                  <button
                    key={item.id}
                    type="button"
                    className="find-row nested"
                    onClick={() => onPick({ kind: 'method', id: item.id, label: item.label, detail: '' })}
                  >
                    <span className="grow">{item.label}</span>
                  </button>
                ))}
              </div>
            ))}
          </div>
        ))}
        {result.truncated && <div className="hint">{t('find.truncated')}</div>}
        </div>
      </div>
    </div>
  )
}

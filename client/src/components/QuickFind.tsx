import { useEffect, useMemo, useRef, useState } from 'react'
import { searchGraph, type SearchHit } from '../graph/search'
import { useI18n } from '../i18n/I18nProvider'
import type { GraphDocument } from '../types/graph'

interface QuickFindProps {
  graph: GraphDocument | null
  onPick: (hit: SearchHit) => void
  onClose: () => void
}

/**
 * `Ctrl+K` 통합 찾기. 진입점·클래스·메서드를 한 입력칸에서 찾는다.
 * 파일 트리 탐색기를 만들지 않고(설계 제약 1) 기존 탐색 축 안에서 검색만 넓히는 장치다.
 * 계산은 `graph/search.ts`의 순수 함수가 하고 여기서는 그리기만 한다.
 */
export function QuickFind({ graph, onPick, onClose }: QuickFindProps) {
  const { t } = useI18n()
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => { inputRef.current?.focus() }, [])

  const result = useMemo(() => searchGraph(graph, query), [graph, query])
  const flat = useMemo(
    () => [...result.entryPoints, ...result.methods, ...result.classes],
    [result])

  useEffect(() => { setActive(0) }, [query])

  const groups: { key: string; label: string; hits: SearchHit[] }[] = [
    { key: 'entryPoints', label: t('panel.entryPoints'), hits: result.entryPoints },
    { key: 'methods', label: t('detail.methods'), hits: result.methods },
    { key: 'classes', label: t('detail.classes'), hits: result.classes },
  ]

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape') {
      onClose()
      return
    }

    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      if (flat.length === 0) return
      const step = event.key === 'ArrowDown' ? 1 : -1
      setActive((current) => (current + step + flat.length) % flat.length)
      return
    }

    if (event.key === 'Enter' && flat[active]) {
      event.preventDefault()
      onPick(flat[active])
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal quick-find"
        role="dialog"
        aria-modal="true"
        aria-label={t('find.title')}
        onClick={(event) => event.stopPropagation()}
        onKeyDown={onKeyDown}
      >
        <input
          ref={inputRef}
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder={t('find.placeholder')}
          aria-label={t('find.title')}
        />
        {query.trim().length === 0 && <div className="hint">{t('find.hint')}</div>}
        {query.trim().length > 0 && flat.length === 0 && (
          <div className="panel-message tight">{t('find.empty')}</div>
        )}
        {groups.filter((group) => group.hits.length > 0).map((group) => (
          <div key={group.key}>
            <div className="side-group">{group.label}</div>
            {group.hits.map((hit) => (
              <button
                key={`${hit.kind}:${hit.id}`}
                type="button"
                className={`find-row${flat[active] === hit ? ' selected' : ''}`}
                onClick={() => onPick(hit)}
              >
                <span className="grow">{hit.label}</span>
                <span className="hint">{hit.detail}</span>
              </button>
            ))}
          </div>
        ))}
        {result.truncated && <div className="hint">{t('find.truncated')}</div>}
      </div>
    </div>
  )
}

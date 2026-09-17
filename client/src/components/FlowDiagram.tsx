import { useCallback, useMemo, useState, type RefObject } from 'react'
import { layoutFlow, type FlowView } from '../graph/flowMap'
import { cullToViewport, type Viewport } from '../graph/treeLayout'
import type { Selection } from '../types/graph'
import { clampZoom, DiagramCanvas, type CanvasPath } from './DiagramCanvas'
import { kindColors } from './kindColors'
import { useI18n } from '../i18n/I18nProvider'

interface FlowDiagramProps {
  view: FlowView
  selection: Selection | null
  zoom: number
  shellRef: RefObject<HTMLDivElement>
  onSelectClass: (classId: string) => void
  onSelectRow: (key: string, rowKind: 'route' | 'method') => void
  onZoomChange?: (zoom: number) => void
}

/** 전체 진입점 지도와 진입점 흐름을 같은 캔버스로 그린다. 차이는 박스 줄의 내용뿐이다. */
export function FlowDiagram({ view, selection, zoom, shellRef, onSelectClass, onSelectRow, onZoomChange }: FlowDiagramProps) {
  const { t } = useI18n()
  const layout = useMemo(() => layoutFlow(view), [view])
  const selectedKey = selection?.key ?? null
  // 박스가 수백 개면 보이는 것만 DOM으로 그린다(여백 600px). 스크롤 80px 이내 변화는 다시 그리지 않는다.
  const [viewport, setViewport] = useState<Viewport | null>(null)
  const onViewportChange = useCallback((next: Viewport) => setViewport((current) =>
    current && Math.abs(current.left - next.left) < 80 && Math.abs(current.top - next.top) < 80 &&
    current.width === next.width && current.height === next.height
      ? current
      : next), [])
  const visibleBoxes = useMemo(
    () => cullToViewport(view.boxes, layout.boxes, viewport),
    [view.boxes, layout.boxes, viewport])

  const fitZoom = useCallback(() => {
    const shell = shellRef.current
    if (!shell || layout.size.width === 0) return
    const fit = Math.min(
      (shell.clientWidth - 40) / Math.max(1, layout.size.width),
      (shell.clientHeight - 40) / Math.max(1, layout.size.height))
    onZoomChange?.(clampZoom(fit))
  }, [shellRef, layout, onZoomChange])

  const paths: CanvasPath[] = layout.paths.map((path, index) => {
    const relation = path.relation
    const active = selectedKey != null &&
      [relation.from, relation.to, relation.fromRow, relation.toRow].includes(selectedKey)
    return {
      id: `${relation.from}-${relation.to}-${index}`,
      d: path.d,
      className: `dg-edge${active ? ' active' : ''}`,
      marker: active ? 'mk-arrow-active' : 'mk-arrow',
    }
  })

  return (
    <DiagramCanvas
      shellRef={shellRef}
      width={layout.size.width}
      height={layout.size.height}
      zoom={zoom}
      paths={paths}
      emptyMessage={view.boxes.length === 0 ? t('flow.empty') : undefined}
      ariaLabel={t('flow.aria')}
      onZoomChange={onZoomChange}
      onFitZoom={onZoomChange ? fitZoom : undefined}
      onViewportChange={onViewportChange}
    >
      {visibleBoxes.map((box) => {
        const placed = layout.boxes.get(box.id)
        if (!placed) return null
        return (
          <div
            key={box.id}
            className={`dg-node clickable${selectedKey === box.id ? ' selected' : ''}`}
            style={{ left: placed.x, top: placed.y, width: placed.w }}
            onClick={() => onSelectClass(box.id)}
            onKeyDown={(event) => {
              if (event.key !== 'Enter' && event.key !== ' ') return
              event.preventDefault()
              onSelectClass(box.id)
            }}
            role="button"
            tabIndex={0}
            aria-label={t('a11y.selectNode', { name: box.title })}
            title={box.id}
          >
            <div className="dg-head">
              <span className="dg-kind" style={{ background: kindColors[box.kind] }} />
              <span className="dg-title">{box.title}</span>
            </div>
            {box.subtitle && <div className="dg-sub">{box.subtitle}</div>}
            {box.rows.length > 0 && (
              <div className="dg-rows">
                {box.rows.map((row) => (
                  <div
                    key={row.key}
                    className={`dg-row${selectedKey === row.key ? ' selected' : ''}`}
                    role="button"
                    tabIndex={0}
                    aria-label={t('a11y.selectNode', { name: row.label })}
                    onClick={(event) => { event.stopPropagation(); onSelectRow(row.key, row.rowKind) }}
                    onKeyDown={(event) => {
                      if (event.key !== 'Enter' && event.key !== ' ') return
                      event.preventDefault()
                      event.stopPropagation()
                      onSelectRow(row.key, row.rowKind)
                    }}
                    title={row.recursive ? t('flow.recursive', { label: row.label }) : row.label}
                  >
                    {row.httpMethod && (
                      <span className={`http ${row.httpMethod.toLowerCase()}`}>{row.httpMethod}</span>
                    )}
                    <span className="grow">{row.label}</span>
                    {row.badge && <span className="more">{row.badge}</span>}
                  </div>
                ))}
              </div>
            )}
          </div>
        )
      })}
    </DiagramCanvas>
  )
}

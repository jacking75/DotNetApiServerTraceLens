import { useCallback, useMemo, useState, type RefObject } from 'react'
import type { ClassDiagramOptions, ClassDiagramView } from '../graph/classDiagram'
import { shortenNamespace } from '../graph/classDiagram'
import { cullToViewport, type Viewport } from '../graph/treeLayout'
import type { RelationType } from '../graph/relations'
import type { GraphClass, GraphClassMember, Selection } from '../types/graph'
import { DiagramCanvas, type CanvasPath } from './DiagramCanvas'
import { UmlClassBox } from './UmlClassBox'
import { useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'

interface ClassDiagramProps {
  view: ClassDiagramView
  options: ClassDiagramOptions
  selection: Selection | null
  selectedClassId: string | null
  selectedMemberKey: string | null
  zoom: number
  shellRef: RefObject<HTMLDivElement>
  scrollTarget: { x: number; y: number; token: number } | null
  notice: string | null
  onSelectClass: (classId: string) => void
  onSelectMember: (graphClass: GraphClass, member: GraphClassMember) => void
  onZoomChange?: (zoom: number) => void
  onFitZoom?: () => void
}

const markers: Record<RelationType, { normal: string; active: string }> = {
  inherit: { normal: 'mk-triangle', active: 'mk-triangle-active' },
  implement: { normal: 'mk-triangle', active: 'mk-triangle-active' },
  assoc: { normal: 'mk-open', active: 'mk-open-active' },
  depend: { normal: 'mk-depend', active: 'mk-open-active' },
}

export function ClassDiagram({
  view,
  options,
  selection,
  selectedClassId,
  selectedMemberKey,
  zoom,
  shellRef,
  scrollTarget,
  notice,
  onSelectClass,
  onSelectMember,
  onZoomChange,
  onFitZoom,
}: ClassDiagramProps) {
  const { t } = useI18n()
  // 클래스가 수백 개면 보이는 박스만 그린다. 스크롤하면 바로 채워진다(여백 600px).
  const [viewport, setViewport] = useState<Viewport | null>(null)
  const onViewportChange = useCallback((next: Viewport) => setViewport((current) =>
    current && Math.abs(current.left - next.left) < 80 && Math.abs(current.top - next.top) < 80 &&
    current.width === next.width && current.height === next.height
      ? current
      : next), [])
  // 선택한 클래스에 붙은 선만 강조하고, 그 선이 닿지 않는 박스는 흐리게 만든다.
  const { paths, related } = useMemo(() => {
    const connected = new Set<string>()
    const canvasPaths: CanvasPath[] = view.paths.map((path, index) => {
      const active = selectedClassId != null && (path.from === selectedClassId || path.to === selectedClassId)
      if (active) {
        connected.add(path.from)
        connected.add(path.to)
      }

      return {
        id: `${path.type}-${path.from}-${path.to}-${index}`,
        d: path.d,
        className: `rel rel-${path.type}${active ? ' active' : ''}`,
        marker: active ? markers[path.type].active : markers[path.type].normal,
      }
    })

    if (selectedClassId) connected.add(selectedClassId)
    return { paths: canvasPaths, related: connected }
  }, [view.paths, selectedClassId])

  const highlightNamespace = selection?.type === 'namespace' ? selection.key : null
  const emptyMessage = view.boxes.size === 0
    ? t('classmap.empty')
    : undefined

  return (
    <>
      {notice && <div className="canvas-notice">{notice}</div>}
      <DiagramCanvas
        shellRef={shellRef}
        width={view.size.width}
        height={view.size.height}
        zoom={zoom}
        paths={paths}
        packages={view.packages}
        packageLabel={(namespace) => shortenNamespace(namespace, options.namespacePrefix)}
        emptyMessage={emptyMessage}
        scrollTarget={scrollTarget}
        ariaLabel={t('classmap.aria')}
        onZoomChange={onZoomChange}
        onFitZoom={onFitZoom}
        onViewportChange={onViewportChange}
      >
        {cullToViewport(view.visible, view.boxes, viewport).map((graphClass) => {
          const box = view.boxes.get(graphClass.id)
          if (!box) return null
          return (
            <UmlClassBox
              key={graphClass.id}
              graphClass={graphClass}
              box={box}
              layout={options.layout}
              namespacePrefix={options.namespacePrefix}
              showFields={options.showFields}
              showMethods={options.showMethods}
              selected={selectedClassId === graphClass.id ||
                (highlightNamespace != null && graphClass.namespace === highlightNamespace)}
              dimmed={selectedClassId != null && !related.has(graphClass.id)}
              selectedMemberKey={selectedMemberKey}
              onSelectClass={onSelectClass}
              onSelectMember={onSelectMember}
            />
          )
        })}
      </DiagramCanvas>
      <ClassDiagramLegend />
    </>
  )
}

function ClassDiagramLegend() {
  const { t } = useI18n()
  const items: Array<{ key: TranslationKey; type: RelationType }> = [
    { key: 'classmap.legend.inherit', type: 'inherit' },
    { key: 'classmap.legend.implement', type: 'implement' },
    { key: 'classmap.legend.assoc', type: 'assoc' },
    { key: 'classmap.legend.depend', type: 'depend' },
  ]

  return (
    <div className="graph-legend">
      {items.map((item) => (
        <span className="legend-item" key={item.type}>
          <svg width="34" height="10" aria-hidden="true">
            <path
              d="M0 5 L30 5"
              className={`rel-${item.type}`}
              fill="none"
              markerEnd={`url(#${item.type === 'inherit' || item.type === 'implement' ? 'mk-triangle' : 'mk-open'})`}
            />
          </svg>
          <span>{t(item.key)}</span>
        </span>
      ))}
      <span className="legend-item">{t('classmap.legend.note')}</span>
    </div>
  )
}

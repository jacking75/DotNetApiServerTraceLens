import cytoscape from 'cytoscape'
import { useCallback, useEffect, useRef, useState } from 'react'
import { buildElements, buildStyle } from '../graph/cytoscapeStyle'
import type { EntryPoint, GraphNode, GraphWindow, NodeKind } from '../types/graph'
import { kindColors } from './kindColors'
import { clampZoom, ZoomControls } from './ZoomControls'
import { useI18n } from '../i18n/I18nProvider'

interface GraphViewProps {
  graphWindow: GraphWindow | null
  entryPoints: EntryPoint[]
  selectedNodeId: string | null
  /** 영향 목록에서 온 경우 진입점 → 대상 경로. 이 경로의 엣지를 굵게 그린다. */
  highlightPath?: readonly string[]
  /** 선택한 노드가 이 창에 없을 때 그 노드까지의 경로를 펼친다. */
  onGoToSelected?: () => void
  onSelectNode: (node: GraphNode) => void
  onDrilldown: (node: GraphNode) => void
  /** 접힌 팬아웃 요약 노드를 펼친다. */
  onExpandGroup: (groupId: string) => void
  /** 캔버스를 PNG 데이터 URI로 넘긴다. 주어지지 않으면 저장 버튼을 그리지 않는다. */
  onExportPng?: (dataUri: string) => void
}

/** 문서 루트의 CSS 변수 값을 읽는다. Cytoscape는 CSS를 모르므로 색을 직접 넘겨야 한다. */
function cssVariable(name: string, fallback: string): string {
  if (typeof window === 'undefined') return fallback
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  return value.length > 0 ? value : fallback
}

export function GraphView({
  graphWindow,
  entryPoints,
  selectedNodeId,
  highlightPath = [],
  onGoToSelected,
  onSelectNode,
  onDrilldown,
  onExpandGroup,
  onExportPng,
}: GraphViewProps) {
  const { t } = useI18n()
  const containerRef = useRef<HTMLDivElement>(null)
  const instanceRef = useRef<cytoscape.Core | null>(null)
  const [zoom, setZoom] = useState(1)

  // 콜백은 ref로 들고 있는다. App이 리렌더될 때마다 새 함수가 와도 그래프를 다시 그리면 안 된다
  // (30초 신선도 틱에 팬·줌이 초기화되던 원인).
  const handlersRef = useRef({ onSelectNode, onDrilldown, onExpandGroup })
  handlersRef.current = { onSelectNode, onDrilldown, onExpandGroup }
  const entryPointsRef = useRef(entryPoints)
  entryPointsRef.current = entryPoints

  useEffect(() => {
    if (!containerRef.current || !graphWindow || graphWindow.nodes.length === 0) {
      return undefined
    }

    const nodeMap = new Map(graphWindow.nodes.map((node) => [node.id, node]))
    const entryPointByNode = new Map(entryPointsRef.current.map((item) => [item.nodeId, item]))
    const groupLabel = (kind: NodeKind, count: number) =>
      `${kind}${String.fromCharCode(10)}${t('graph.fanoutGroup', { kind: '', count }).trim()}`
    const instance = cytoscape({
      container: containerRef.current,
      elements: buildElements(graphWindow, kindColors, entryPointByNode, groupLabel),
      style: buildStyle({
        label: cssVariable('--graph-label', '#f8fafc'),
        nodeBorder: cssVariable('--graph-node-border', 'rgba(255, 255, 255, 0.19)'),
        surface: cssVariable('--graph-node-bg', '#1b2536'),
        edge: cssVariable('--canvas-edge', '#657086'),
        accent: cssVariable('--accent', '#8ddcff'),
      }, kindColors),
      // 레벨 간 간격을 줄인다. 예전에는 노드 6개가 화면을 다 썼다.
      layout: { name: 'breadthfirst', directed: true, spacingFactor: 0.85, padding: 24 },
      // 맨 휠은 스크롤, Ctrl+휠이 줌이다. 세 캔버스에서 같게 맞춘다.
      userZoomingEnabled: false,
    })

    instance.on('tap', 'node', (event) => {
      const id = event.target.id() as string
      if (id.startsWith('fanout:')) {
        handlersRef.current.onExpandGroup(id)
        return
      }

      const currentNode = nodeMap.get(id)
      if (!currentNode) return
      handlersRef.current.onSelectNode(currentNode)
      // Shift+클릭은 파고들지 않고 선택만 한다. 상세만 보고 싶은데 창이 바뀌면 되돌아오기 번거롭다.
      const shiftHeld = (event.originalEvent as MouseEvent | undefined)?.shiftKey === true
      if (!shiftHeld && graphWindow.branchNodeIds.has(currentNode.id)) {
        handlersRef.current.onDrilldown(currentNode)
      }
    })

    // 창이 바뀌면 화면을 맞춘다. 파고든 뒤 포커스가 화면 아래에 걸리던 문제가 여기서 사라진다.
    instance.ready(() => {
      instance.fit(undefined, 28)
      setZoom(Number(instance.zoom().toFixed(2)))
    })
    instance.on('zoom', () => setZoom(Number(instance.zoom().toFixed(2))))

    instanceRef.current = instance
    return () => {
      instanceRef.current = null
      instance.destroy()
    }
  }, [graphWindow])

  // Ctrl+휠만 줌으로 받는다. 맨 휠은 캔버스를 위아래로 옮긴다.
  useEffect(() => {
    const container = containerRef.current
    if (!container) return
    const onWheel = (event: WheelEvent) => {
      const instance = instanceRef.current
      if (!instance) return
      if (event.ctrlKey || event.metaKey) {
        event.preventDefault()
        const next = clampZoom(instance.zoom() * (event.deltaY < 0 ? 1.1 : 0.9))
        instance.zoom({ level: next, renderedPosition: { x: event.offsetX, y: event.offsetY } })
        return
      }

      event.preventDefault()
      instance.panBy({ x: -event.deltaX, y: -event.deltaY })
    }

    container.addEventListener('wheel', onWheel, { passive: false })
    return () => container.removeEventListener('wheel', onWheel)
  }, [])

  const changeZoom = useCallback((next: number) => {
    const instance = instanceRef.current
    if (!instance) return
    instance.zoom({ level: next, renderedPosition: { x: instance.width() / 2, y: instance.height() / 2 } })
  }, [])

  const fit = useCallback(() => {
    instanceRef.current?.fit(undefined, 28)
  }, [])

  /** 호출 흐름은 Cytoscape가 그리므로 SVG 대신 PNG로 저장한다. */
  const exportPng = useCallback(() => {
    const instance = instanceRef.current
    if (!instance) return
    onExportPng?.(instance.png({ full: true, scale: 2, bg: cssVariable('--surface-1', '#0f1420') }))
  }, [onExportPng])

  // 선택 강조만 바꿀 때는 그래프를 다시 만들지 않는다.
  useEffect(() => {
    const instance = instanceRef.current
    if (!instance) return
    instance.nodes().removeClass('selected')
    if (selectedNodeId) instance.$id(selectedNodeId).addClass('selected')
  }, [selectedNodeId, graphWindow])

  // 영향 목록에서 온 경로를 굵게 그린다.
  useEffect(() => {
    const instance = instanceRef.current
    if (!instance) return
    instance.elements().removeClass('on-path')
    const onPath = new Set(highlightPath)
    for (const id of onPath) instance.$id(id).addClass('on-path')
    instance.edges().forEach((edge) => {
      const index = highlightPath.indexOf(edge.data('source') as string)
      if (index >= 0 && highlightPath[index + 1] === edge.data('target')) edge.addClass('on-path')
    })
  }, [highlightPath, graphWindow])

  /** 선택한 노드가 이 창에 없으면 조용히 넘어가지 않고 이동 수단을 준다. */
  const selectedOutOfWindow = Boolean(
    selectedNodeId && graphWindow && !graphWindow.nodes.some((node) => node.id === selectedNodeId))

  return (
    <div className="graph-shell">
      <div className="graph-help">
        {t('graph.expandHint')}
        {graphWindow?.hiddenCount
          ? ` ${t('graph.windowLimit', { shown: graphWindow.nodes.length, hidden: graphWindow.hiddenCount })}`
          : ''}
        {graphWindow?.direction === 'up' ? ` ${t('graph.callerWindow')}` : ''}
        {highlightPath.length > 0 ? ` ${t('graph.pathHighlight')}` : ''}
      </div>
      {selectedOutOfWindow && onGoToSelected && (
        <div className="graph-outside" role="status">
          <span>{t('graph.selectionOutside')}</span>
          <button type="button" className="link-button" onClick={onGoToSelected}>{t('graph.goToSelection')}</button>
        </div>
      )}
      {(!graphWindow || graphWindow.nodes.length === 0) && (
        <div className="panel-message canvas-empty">
          {graphWindow ? t('graph.emptyWindow') : t('graph.pickEntryPoint')}
        </div>
      )}
      <div className="graph-canvas" ref={containerRef} aria-label={t('graph.aria')} />
      {graphWindow && graphWindow.nodes.length > 0 && (
        <ZoomControls zoom={zoom} onZoomChange={changeZoom} onFitZoom={fit} />
      )}
      {graphWindow && graphWindow.nodes.length > 0 && onExportPng && (
        <button type="button" className="canvas-export" onClick={exportPng}>{t('export.savePng')}</button>
      )}
    </div>
  )
}

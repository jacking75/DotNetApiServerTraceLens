import cytoscape from 'cytoscape'
import { useEffect, useRef } from 'react'
import type { GraphNode, GraphWindow } from '../types/graph'
import { kindColors } from './kindColors'
import { useI18n } from '../i18n/I18nProvider'

interface GraphViewProps {
  graphWindow: GraphWindow | null
  selectedNodeId: string | null
  /** 영향 목록에서 온 경우 진입점 → 대상 경로. 이 경로의 엣지를 굵게 그린다. */
  highlightPath?: readonly string[]
  onSelectNode: (node: GraphNode) => void
  onDrilldown: (node: GraphNode) => void
}

/** 문서 루트의 CSS 변수 값을 읽는다. Cytoscape는 CSS를 모르므로 색을 직접 넘겨야 한다. */
function cssVariable(name: string, fallback: string): string {
  if (typeof window === 'undefined') return fallback
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  return value.length > 0 ? value : fallback
}

export function GraphView({
  graphWindow,
  selectedNodeId,
  highlightPath = [],
  onSelectNode,
  onDrilldown,
}: GraphViewProps) {
  const { t } = useI18n()
  const containerRef = useRef<HTMLDivElement>(null)
  const instanceRef = useRef<cytoscape.Core | null>(null)

  // 콜백은 ref로 들고 있는다. App이 리렌더될 때마다 새 함수가 와도 그래프를 다시 그리면 안 된다
  // (30초 신선도 틱에 팬·줌이 초기화되던 원인).
  const handlersRef = useRef({ onSelectNode, onDrilldown })
  handlersRef.current = { onSelectNode, onDrilldown }

  useEffect(() => {
    if (!containerRef.current || !graphWindow || graphWindow.nodes.length === 0) {
      return undefined
    }

    const nodeMap = new Map(graphWindow.nodes.map((node) => [node.id, node]))
    const instance = cytoscape({
      container: containerRef.current,
      elements: [
        ...graphWindow.nodes.map((node) => ({
          data: {
            id: node.id,
            label: `${graphWindow.branchNodeIds.has(node.id) ? '⊕  ' : ''}${node.displayName}`,
            color: kindColors[node.kind],
          },
        })),
        ...graphWindow.edges.map((edge, index) => ({
          data: {
            id: `edge-${index}-${edge.from}-${edge.to}`,
            source: edge.from,
            target: edge.to,
          },
          classes: edge.confidence === 'Estimated' ? 'estimated' : '',
        })),
      ],
      style: [
        {
          selector: 'node',
          style: {
            'background-color': 'data(color)',
            // 라벨·선 색은 CSS 변수에서 읽는다. 라이트 테마에서 흰 글씨가 남으면 읽을 수 없다.
            color: cssVariable('--graph-label', '#f8fafc'),
            label: 'data(label)',
            'font-size': 12,
            'font-weight': 600,
            'text-wrap': 'wrap',
            'text-max-width': '175px',
            width: 210,
            height: 48,
            shape: 'round-rectangle',
            'border-width': 1,
            'border-color': cssVariable('--graph-node-border', '#ffffff30'),
          },
        },
        {
          selector: 'node.selected',
          style: { 'border-width': 3, 'border-color': '#8ddcff' },
        },
        {
          selector: 'edge',
          style: {
            width: 2,
            'line-color': cssVariable('--canvas-edge', '#657086'),
            'target-arrow-color': cssVariable('--canvas-edge', '#657086'),
            'target-arrow-shape': 'triangle',
            'curve-style': 'bezier',
          },
        },
        {
          // 람다 본문 안의 호출만 있는 엣지는 추정이다. 실선과 구분해서 그린다.
          selector: 'edge.estimated',
          style: { 'line-style': 'dashed' },
        },
        {
          selector: 'edge.on-path',
          style: { width: 4, 'line-color': '#8ddcff', 'target-arrow-color': '#8ddcff' },
        },
        {
          selector: 'node.on-path',
          style: { 'border-width': 2, 'border-color': '#8ddcff' },
        },
      ],
      layout: { name: 'breadthfirst', directed: true, spacingFactor: 1.25, padding: 30 },
    })

    instance.on('tap', 'node', (event) => {
      const currentNode = nodeMap.get(event.target.id())
      if (!currentNode) return
      handlersRef.current.onSelectNode(currentNode)
      // Shift+클릭은 파고들지 않고 선택만 한다. 상세만 보고 싶은데 창이 바뀌면 되돌아오기 번거롭다.
      const shiftHeld = (event.originalEvent as MouseEvent | undefined)?.shiftKey === true
      if (!shiftHeld && graphWindow.branchNodeIds.has(currentNode.id)) {
        handlersRef.current.onDrilldown(currentNode)
      }
    })

    instanceRef.current = instance
    return () => {
      instanceRef.current = null
      instance.destroy()
    }
  }, [graphWindow])

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

  return (
    <div className="graph-shell">
      <div className="graph-help">
        {t('graph.expandHint')}
        {graphWindow?.hiddenCount
          ? ` ${t('graph.windowLimit', { shown: graphWindow.nodes.length, hidden: graphWindow.hiddenCount })}`
          : ''}
        {highlightPath.length > 0 ? ` ${t('graph.pathHighlight')}` : ''}
      </div>
      {(!graphWindow || graphWindow.nodes.length === 0) && (
        <div className="panel-message canvas-empty">
          {graphWindow ? t('graph.emptyWindow') : t('graph.pickEntryPoint')}
        </div>
      )}
      <div className="graph-canvas" ref={containerRef} aria-label={t('graph.aria')} />
    </div>
  )
}

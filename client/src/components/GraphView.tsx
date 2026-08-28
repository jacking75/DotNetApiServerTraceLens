import cytoscape from 'cytoscape'
import { useEffect, useRef } from 'react'
import type { GraphNode, GraphWindow } from '../types/graph'

interface GraphViewProps {
  graphWindow: GraphWindow | null
  selectedNodeId: string | null
  onSelectNode: (node: GraphNode) => void
  onDrilldown: (node: GraphNode) => void
}

const kindColors: Record<string, string> = {
  Controller: '#7c6df2',
  Service: '#268f81',
  Repository: '#c57b35',
  Validator: '#b75b8d',
  Infra: '#547aa5',
  Db: '#9b6fca',
  Method: '#566174',
}

const groupNodeId = (classId: string) => `class-group:${classId}`

export function GraphView({ graphWindow, selectedNodeId, onSelectNode, onDrilldown }: GraphViewProps) {
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!containerRef.current || !graphWindow || graphWindow.nodes.length === 0) {
      return undefined
    }

    const nodeMap = new Map(graphWindow.nodes.map((node) => [node.id, node]))
    const groupClassId = graphWindow.groupClassId
    const hasGroup = groupClassId !== null && graphWindow.nodes.some((node) => node.classId === groupClassId)
    const instance = cytoscape({
      container: containerRef.current,
      elements: [
        ...(hasGroup && groupClassId
          ? [{ data: { id: groupNodeId(groupClassId), label: graphWindow.groupLabel ?? groupClassId } }]
          : []),
        ...graphWindow.nodes.map((node) => ({
          data: {
            id: node.id,
            label: `${graphWindow.branchNodeIds.has(node.id) ? '⊕  ' : ''}${node.displayName}`,
            color: kindColors[node.kind],
            parent: hasGroup && groupClassId && node.classId === groupClassId ? groupNodeId(groupClassId) : undefined,
          },
          classes: node.id === selectedNodeId ? 'selected' : '',
        })),
        ...graphWindow.edges.map((edge, index) => ({
          data: { id: `edge-${index}-${edge.from}-${edge.to}`, source: edge.from, target: edge.to },
        })),
      ],
      style: [
        {
          selector: 'node',
          style: {
            'background-color': 'data(color)',
            color: '#f8fafc',
            label: 'data(label)',
            'font-size': 12,
            'font-weight': 600,
            'text-wrap': 'wrap',
            'text-max-width': '175px',
            width: 210,
            height: 48,
            shape: 'round-rectangle',
            'border-width': 1,
            'border-color': '#ffffff30',
          },
        },
        {
          selector: 'node.selected',
          style: { 'border-width': 3, 'border-color': '#8ddcff' },
        },
        {
          selector: ':parent',
          style: {
            'background-color': '#182339',
            'background-opacity': 0.75,
            'border-width': 1,
            'border-color': '#3a4b66',
            color: '#9fb0c9',
            'font-size': 11,
            'text-valign': 'top',
            'text-halign': 'center',
            'text-margin-y': -10,
            padding: '34px',
            width: 'label',
            height: 'label',
          },
        },
        {
          selector: 'edge',
          style: {
            width: 2,
            'line-color': '#657086',
            'target-arrow-color': '#657086',
            'target-arrow-shape': 'triangle',
            'curve-style': 'bezier',
          },
        },
      ],
      layout: { name: 'breadthfirst', directed: true, spacingFactor: 1.25, padding: 30 },
    })

    instance.on('tap', 'node', (event) => {
      const currentNode = nodeMap.get(event.target.id())
      if (!currentNode) return
      onSelectNode(currentNode)
      if (graphWindow.branchNodeIds.has(currentNode.id)) {
        onDrilldown(currentNode)
      }
    })

    return () => instance.destroy()
  }, [graphWindow, onDrilldown, onSelectNode, selectedNodeId])

  return (
    <div className="graph-shell">
      <div className="graph-help">
        {graphWindow?.groupClassId
          ? '박스 안이 선택한 클래스의 메서드다. 위쪽은 호출자, 아래쪽은 호출 대상이다.'
          : '⊕가 붙은 분기 노드를 클릭하면 하위 흐름을 펼친다.'}
      </div>
      {graphWindow && graphWindow.nodes.length === 0 && (
        <div className="panel-message">이 클래스에는 그래프에 담긴 메서드가 없다.</div>
      )}
      <div className="graph-canvas" ref={containerRef} aria-label="호출 흐름 그래프" />
    </div>
  )
}

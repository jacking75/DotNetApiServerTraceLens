import cytoscape from 'cytoscape'
import { useEffect, useRef } from 'react'
import type { GraphNode, GraphWindow } from '../types/graph'
import { kindColors } from './kindColors'
import { useI18n } from '../i18n/I18nProvider'

interface GraphViewProps {
  graphWindow: GraphWindow | null
  selectedNodeId: string | null
  onSelectNode: (node: GraphNode) => void
  onDrilldown: (node: GraphNode) => void
}

export function GraphView({ graphWindow, selectedNodeId, onSelectNode, onDrilldown }: GraphViewProps) {
  const { t } = useI18n()
  const containerRef = useRef<HTMLDivElement>(null)

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
      <div className="graph-help">{t('graph.expandHint')}</div>
      {(!graphWindow || graphWindow.nodes.length === 0) && (
        <div className="panel-message canvas-empty">
          {graphWindow ? t('graph.emptyWindow') : t('graph.pickEntryPoint')}
        </div>
      )}
      <div className="graph-canvas" ref={containerRef} aria-label={t('graph.aria')} />
    </div>
  )
}

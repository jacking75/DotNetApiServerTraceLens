import { lazy, Suspense, useCallback, useState } from 'react'
import { analyzeSolution, getEntryPoints, getGraph } from './api/client'
import { AskPanel } from './components/AskPanel'
import { Breadcrumb, type BreadcrumbItem } from './components/Breadcrumb'
import { findNode, getDrilldownWindow, getInitialWindow } from './graph/windowing'
import type { AnalyzeSummary, EntryPoint, GraphDocument, GraphNode, GraphWindow } from './types/graph'

const CodePanel = lazy(() => import('./components/CodePanel').then((module) => ({ default: module.CodePanel })))
const GraphView = lazy(() => import('./components/GraphView').then((module) => ({ default: module.GraphView })))

export default function App() {
  const [solutionPath, setSolutionPath] = useState('')
  const [summary, setSummary] = useState<AnalyzeSummary | null>(null)
  const [entryPoints, setEntryPoints] = useState<EntryPoint[]>([])
  const [graph, setGraph] = useState<GraphDocument | null>(null)
  const [graphWindow, setGraphWindow] = useState<GraphWindow | null>(null)
  const [selectedNode, setSelectedNode] = useState<GraphNode | null>(null)
  const [breadcrumbs, setBreadcrumbs] = useState<BreadcrumbItem[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const analyze = async () => {
    if (!solutionPath.trim()) return
    setBusy(true)
    setError(null)
    setGraph(null)
    setGraphWindow(null)
    setSelectedNode(null)
    try {
      const nextSummary = await analyzeSolution(solutionPath.trim())
      const nextEntryPoints = await getEntryPoints()
      setSummary(nextSummary)
      setEntryPoints(nextEntryPoints)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '분석에 실패했다.')
    } finally {
      setBusy(false)
    }
  }

  const selectEntryPoint = async (entryPoint: EntryPoint) => {
    setBusy(true)
    setError(null)
    try {
      const nextGraph = await getGraph()
      const initialWindow = getInitialWindow(nextGraph, entryPoint.nodeId)
      const node = findNode(nextGraph, entryPoint.nodeId) ?? null
      setGraph(nextGraph)
      setGraphWindow(initialWindow)
      setSelectedNode(node)
      setBreadcrumbs([{ nodeId: entryPoint.nodeId, label: node?.displayName ?? entryPoint.route }])
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '그래프를 불러오지 못했다.')
    } finally {
      setBusy(false)
    }
  }

  const drilldown = useCallback((node: GraphNode) => {
    if (!graph || graphWindow?.focusNodeId === node.id) return
    setGraphWindow(getDrilldownWindow(graph, node.id))
    setBreadcrumbs((current) => [...current, { nodeId: node.id, label: node.displayName }])
  }, [graph, graphWindow?.focusNodeId])

  const navigateBreadcrumb = (index: number) => {
    if (!graph) return
    const target = breadcrumbs[index]
    setBreadcrumbs((current) => current.slice(0, index + 1))
    setGraphWindow(index === 0
      ? getInitialWindow(graph, target.nodeId)
      : getDrilldownWindow(graph, target.nodeId))
    setSelectedNode(findNode(graph, target.nodeId) ?? null)
  }

  return (
    <main>
      <header className="app-header">
        <div>
          <div className="eyebrow">ASP.NET Core call explorer</div>
          <h1>TraceLens</h1>
        </div>
        <form onSubmit={(event) => { event.preventDefault(); void analyze() }}>
          <input
            value={solutionPath}
            onChange={(event) => setSolutionPath(event.target.value)}
            placeholder="C:\\repos\\MyApp\\MyApp.sln"
            aria-label="솔루션 경로"
          />
          <button type="submit" className="primary" disabled={busy || !solutionPath.trim()}>
            {busy ? '처리 중…' : '분석'}
          </button>
        </form>
      </header>

      {error && <div className="global-error">{error}</div>}
      {summary && (
        <div className="analysis-summary">
          진입점 {summary.entryPointCount} · 노드 {summary.nodeCount} · 엣지 {summary.edgeCount} · {summary.elapsedMs}ms
        </div>
      )}

      <section className="workspace">
        <aside className="entrypoints panel">
          <header><span>Entry points</span><small>{entryPoints.length}개</small></header>
          <div className="entrypoint-list">
            {entryPoints.length === 0 && <div className="panel-message">솔루션을 분석하면 HTTP 진입점이 표시된다.</div>}
            {entryPoints.map((entryPoint) => (
              <button type="button" key={entryPoint.nodeId} onClick={() => void selectEntryPoint(entryPoint)}>
                <span className={`http-method ${entryPoint.httpMethod.toLowerCase()}`}>{entryPoint.httpMethod}</span>
                <span>{entryPoint.route}</span>
              </button>
            ))}
          </div>
        </aside>

        <section className="graph-area panel">
          <header>
            <span>Call flow</span>
            <small>{graphWindow ? `${graphWindow.nodes.length} nodes` : '진입점을 선택해야 한다'}</small>
          </header>
          <Breadcrumb items={breadcrumbs} onNavigate={navigateBreadcrumb} />
          <Suspense fallback={<div className="panel-message">그래프 뷰를 불러오는 중이다.</div>}>
            <GraphView
              graphWindow={graphWindow}
              selectedNodeId={selectedNode?.id ?? null}
              onSelectNode={setSelectedNode}
              onDrilldown={drilldown}
            />
          </Suspense>
        </section>

        <div className="right-column">
          <Suspense fallback={<section className="panel panel-message">코드 뷰를 불러오는 중이다.</section>}>
            <CodePanel node={selectedNode} />
          </Suspense>
          <AskPanel node={selectedNode} />
        </div>
      </section>
    </main>
  )
}

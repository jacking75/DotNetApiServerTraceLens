import { lazy, Suspense, useCallback, useMemo, useState } from 'react'
import { analyzeTarget, getClasses, getEntryPoints, getGraph } from './api/client'
import { AskPanel } from './components/AskPanel'
import { Breadcrumb, type BreadcrumbItem } from './components/Breadcrumb'
import { FreshnessBadge } from './components/FreshnessBadge'
import { PathPicker } from './components/PathPicker'
import {
  findClassMemberNodes,
  findNode,
  getClassWindow,
  getDrilldownWindow,
  getInitialWindow,
} from './graph/windowing'
import type {
  AnalyzeSummary,
  EntryPoint,
  GraphClassSummary,
  GraphDocument,
  GraphNode,
  GraphWindow,
} from './types/graph'

const CodePanel = lazy(() => import('./components/CodePanel').then((module) => ({ default: module.CodePanel })))
const GraphView = lazy(() => import('./components/GraphView').then((module) => ({ default: module.GraphView })))

/** 클래스가 많은 솔루션에서 목록 렌더링이 무거워지지 않도록 한 번에 보여줄 개수를 제한한다. */
const maxVisibleClasses = 200

type SidebarTab = 'entrypoints' | 'classes'

interface GraphRoot {
  type: 'entryPoint' | 'class'
  id: string
}

const formatTime = (value: string) => new Intl.DateTimeFormat('ko-KR', {
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
}).format(new Date(value))

export default function App() {
  const [solutionPath, setSolutionPath] = useState('')
  const [pickerOpen, setPickerOpen] = useState(false)
  const [summary, setSummary] = useState<AnalyzeSummary | null>(null)
  const [entryPoints, setEntryPoints] = useState<EntryPoint[]>([])
  const [classes, setClasses] = useState<GraphClassSummary[]>([])
  const [sidebarTab, setSidebarTab] = useState<SidebarTab>('entrypoints')
  const [classFilter, setClassFilter] = useState('')
  const [graph, setGraph] = useState<GraphDocument | null>(null)
  const [graphWindow, setGraphWindow] = useState<GraphWindow | null>(null)
  const [selectedNode, setSelectedNode] = useState<GraphNode | null>(null)
  const [selectedRoot, setSelectedRoot] = useState<GraphRoot | null>(null)
  const [breadcrumbs, setBreadcrumbs] = useState<BreadcrumbItem[]>([])
  const [freshnessRevision, setFreshnessRevision] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const filteredClasses = useMemo(() => {
    const keyword = classFilter.trim().toLowerCase()
    if (!keyword) return classes
    return classes.filter((item) => `${item.namespace}.${item.name}`.toLowerCase().includes(keyword))
  }, [classes, classFilter])

  const clearSelection = () => {
    setGraph(null)
    setGraphWindow(null)
    setSelectedNode(null)
    setSelectedRoot(null)
    setBreadcrumbs([])
  }

  const showEntryPointWindow = (nextGraph: GraphDocument, entryPointId: string, fallbackLabel: string) => {
    const node = findNode(nextGraph, entryPointId) ?? null
    setGraph(nextGraph)
    setGraphWindow(getInitialWindow(nextGraph, entryPointId))
    setSelectedNode(node)
    setSelectedRoot({ type: 'entryPoint', id: entryPointId })
    setBreadcrumbs([{ nodeId: entryPointId, label: node?.displayName ?? fallbackLabel }])
  }

  const showClassWindow = (nextGraph: GraphDocument, classId: string, label: string) => {
    setGraph(nextGraph)
    setGraphWindow(getClassWindow(nextGraph, classId))
    setSelectedNode(findClassMemberNodes(nextGraph, classId)[0] ?? null)
    setSelectedRoot({ type: 'class', id: classId })
    setBreadcrumbs([{ nodeId: classId, label }])
  }

  const restoreSelection = async (
    root: GraphRoot,
    nextEntryPoints: EntryPoint[],
    nextClasses: GraphClassSummary[],
  ) => {
    if (root.type === 'entryPoint') {
      const entryPoint = nextEntryPoints.find((item) => item.nodeId === root.id)
      if (!entryPoint) {
        clearSelection()
        return
      }

      showEntryPointWindow(await getGraph(), entryPoint.nodeId, entryPoint.route)
      return
    }

    const graphClass = nextClasses.find((item) => item.id === root.id)
    if (!graphClass) {
      clearSelection()
      return
    }

    showClassWindow(await getGraph(), graphClass.id, graphClass.name)
  }

  const analyze = async (isReanalysis = false) => {
    if (!solutionPath.trim()) return
    setBusy(true)
    setError(null)
    if (!isReanalysis) {
      setSummary(null)
      setEntryPoints([])
      setClasses([])
      clearSelection()
    }
    try {
      const nextSummary = await analyzeTarget(solutionPath.trim())
      const nextEntryPoints = await getEntryPoints()
      const nextClasses = await getClasses()
      setSummary(nextSummary)
      setEntryPoints(nextEntryPoints)
      setClasses(nextClasses)
      if (isReanalysis && selectedRoot) {
        await restoreSelection(selectedRoot, nextEntryPoints, nextClasses)
      }
      setFreshnessRevision((current) => current + 1)
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
      showEntryPointWindow(await getGraph(), entryPoint.nodeId, entryPoint.route)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '그래프를 불러오지 못했다.')
    } finally {
      setBusy(false)
    }
  }

  const selectClass = async (graphClass: GraphClassSummary) => {
    setBusy(true)
    setError(null)
    try {
      showClassWindow(await getGraph(), graphClass.id, graphClass.name)
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
    if (index > 0) {
      setGraphWindow(getDrilldownWindow(graph, target.nodeId))
      setSelectedNode(findNode(graph, target.nodeId) ?? null)
      return
    }

    if (selectedRoot?.type === 'class') {
      setGraphWindow(getClassWindow(graph, target.nodeId))
      setSelectedNode(findClassMemberNodes(graph, target.nodeId)[0] ?? null)
      return
    }

    setGraphWindow(getInitialWindow(graph, target.nodeId))
    setSelectedNode(findNode(graph, target.nodeId) ?? null)
  }

  return (
    <main>
      <header className="app-header">
        <div>
          <div className="eyebrow">ASP.NET Core call explorer</div>
          <h1>TraceLens</h1>
        </div>
        <FreshnessBadge
          canReanalyze={summary !== null}
          refreshKey={freshnessRevision}
          analyzing={busy}
          onReanalyze={() => analyze(true)}
        />
        <form onSubmit={(event) => { event.preventDefault(); void analyze(false) }}>
          <input
            value={solutionPath}
            onChange={(event) => setSolutionPath(event.target.value)}
            placeholder="C:\repos\MyApp\MyApp.sln 또는 MyApp.Api.csproj"
            aria-label="솔루션 또는 프로젝트 경로"
          />
          <button type="button" className="browse" onClick={() => setPickerOpen(true)} disabled={busy}>
            찾아보기…
          </button>
          <button type="submit" className="primary" disabled={busy || !solutionPath.trim()}>
            {busy ? '처리 중…' : '분석'}
          </button>
        </form>
      </header>

      {pickerOpen && (
        <PathPicker
          initialPath={solutionPath}
          onSelect={(path) => { setSolutionPath(path); setPickerOpen(false) }}
          onClose={() => setPickerOpen(false)}
        />
      )}

      {error && <div className="global-error">{error}</div>}
      {summary && (
        <div className="analysis-summary">
          진입점 {summary.entryPointCount} · 클래스 {summary.classCount} · 노드 {summary.nodeCount} · 엣지 {summary.edgeCount} · {summary.elapsedMs}ms
          {summary.previousAnalyzedAt && (
            <> · 재분석 {formatTime(summary.previousAnalyzedAt)} → {formatTime(summary.analyzedAt)}</>
          )}
        </div>
      )}

      <section className="workspace">
        <aside className="entrypoints panel">
          <header>
            <div className="panel-tabs" role="tablist">
              <button
                type="button"
                role="tab"
                aria-selected={sidebarTab === 'entrypoints'}
                className={sidebarTab === 'entrypoints' ? 'active' : ''}
                onClick={() => setSidebarTab('entrypoints')}
              >
                진입점
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={sidebarTab === 'classes'}
                className={sidebarTab === 'classes' ? 'active' : ''}
                onClick={() => setSidebarTab('classes')}
              >
                클래스
              </button>
            </div>
            <small>
              {sidebarTab === 'entrypoints' ? `${entryPoints.length}개` : `${filteredClasses.length}/${classes.length}`}
            </small>
          </header>

          {sidebarTab === 'entrypoints' ? (
            <div className="entrypoint-list">
              {entryPoints.length === 0 && <div className="panel-message">솔루션을 분석하면 HTTP 진입점이 표시된다.</div>}
              {entryPoints.map((entryPoint) => (
                <button
                  type="button"
                  key={entryPoint.nodeId}
                  className={selectedRoot?.type === 'entryPoint' && selectedRoot.id === entryPoint.nodeId ? 'selected' : ''}
                  onClick={() => void selectEntryPoint(entryPoint)}
                >
                  <span className={`http-method ${entryPoint.httpMethod.toLowerCase()}`}>{entryPoint.httpMethod}</span>
                  <span>{entryPoint.route}</span>
                </button>
              ))}
            </div>
          ) : (
            <>
              {classes.length > 0 && (
                <input
                  className="class-filter"
                  value={classFilter}
                  onChange={(event) => setClassFilter(event.target.value)}
                  placeholder="클래스 또는 네임스페이스 검색"
                  aria-label="클래스 검색"
                />
              )}
              <div className="entrypoint-list class-list">
                {classes.length === 0 && <div className="panel-message">솔루션을 분석하면 클래스 목록이 표시된다.</div>}
                {classes.length > 0 && filteredClasses.length === 0 && (
                  <div className="panel-message">검색어와 일치하는 클래스가 없다.</div>
                )}
                {filteredClasses.slice(0, maxVisibleClasses).map((graphClass) => (
                  <button
                    type="button"
                    key={graphClass.id}
                    className={selectedRoot?.type === 'class' && selectedRoot.id === graphClass.id ? 'selected' : ''}
                    onClick={() => void selectClass(graphClass)}
                    title={graphClass.id}
                  >
                    <span className="class-row">
                      <span className={`class-kind ${graphClass.kind.toLowerCase()}`}>{graphClass.kind}</span>
                      <span className="class-name">{graphClass.name}</span>
                      <span className="class-methods">{graphClass.methodCount}</span>
                    </span>
                    <span className="class-namespace">{graphClass.namespace || '(전역 네임스페이스)'}</span>
                  </button>
                ))}
                {filteredClasses.length > maxVisibleClasses && (
                  <div className="panel-message">
                    {filteredClasses.length - maxVisibleClasses}개를 더 찾았다. 검색어로 범위를 좁혀야 한다.
                  </div>
                )}
              </div>
            </>
          )}
        </aside>

        <section className="graph-area panel">
          <header>
            <span>{selectedRoot?.type === 'class' ? 'Class calls' : 'Call flow'}</span>
            <small>{graphWindow ? `${graphWindow.nodes.length} nodes` : '진입점이나 클래스를 선택해야 한다'}</small>
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

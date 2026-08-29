import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { analyzeTarget, getEntryPoints, getGraph, getNamespaces } from './api/client'
import { AskPanel, type AskContext } from './components/AskPanel'
import { Breadcrumb, type BreadcrumbItem } from './components/Breadcrumb'
import { ClassDiagram } from './components/ClassDiagram'
import { DetailPanel, type DetailActions } from './components/DetailPanel'
import { FlowDiagram } from './components/FlowDiagram'
import { FreshnessBadge } from './components/FreshnessBadge'
import { PathPicker } from './components/PathPicker'
import { EntryPointList, FlowSidePanel, NamespacePanel } from './components/SidePanels'
import { Splitter } from './components/Splitter'
import { ViewToolbar } from './components/ViewToolbar'
import {
  buildClassDiagram,
  commonNamespacePrefix,
  largeSolutionThreshold,
  memberKey,
  shortenNamespace,
  type ClassDiagramOptions,
} from './graph/classDiagram'
import { buildEntryFlow, buildEntryMap } from './graph/flowMap'
import { collapseEdgesToClasses, deriveRelations } from './graph/relations'
import { getDrilldownWindow, getInitialWindow } from './graph/windowing'
import {
  defaultColumns,
  loadState,
  saveState,
  type ClassMapState,
  type PersistedState,
} from './state/viewState'
import type {
  AnalyzeSummary,
  EntryPoint,
  GraphClass,
  GraphClassMember,
  GraphDocument,
  GraphNamespaceSummary,
  GraphWindow,
  Selection,
  ViewMode,
} from './types/graph'

const GraphView = lazy(() => import('./components/GraphView').then((module) => ({ default: module.GraphView })))

const minCenter = 360
const minLeft = 190
const minRight = 280
/** 창 크기가 바뀔 때 배치를 다시 계산하기까지 기다리는 시간. */
const resizeDebounceMs = 120

const formatTime = (value: string) => new Intl.DateTimeFormat('ko-KR', {
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
}).format(new Date(value))

export default function App() {
  const initial = useRef<PersistedState>(loadState()).current
  const [solutionPath, setSolutionPath] = useState(initial.solutionPath)
  const [pickerOpen, setPickerOpen] = useState(false)
  const [summary, setSummary] = useState<AnalyzeSummary | null>(null)
  const [entryPoints, setEntryPoints] = useState<EntryPoint[]>([])
  const [namespaces, setNamespaces] = useState<GraphNamespaceSummary[]>([])
  const [graph, setGraph] = useState<GraphDocument | null>(null)

  const [viewMode, setViewMode] = useState<ViewMode>(initial.viewMode)
  const [classMap, setClassMap] = useState<ClassMapState>(initial.classMap)
  const [depth, setDepth] = useState(initial.depth)
  const [selection, setSelection] = useState<Selection | null>(initial.selection)
  const [search, setSearch] = useState('')
  const [columns, setColumns] = useState(initial.columns)

  const [callFlowEntry, setCallFlowEntry] = useState<EntryPoint | null>(null)
  const [focusStack, setFocusStack] = useState<string[]>([])
  const [flowEntryId, setFlowEntryId] = useState<string | null>(null)

  const [scrollTarget, setScrollTarget] = useState<{ x: number; y: number; token: number } | null>(null)
  const [pendingScrollNamespace, setPendingScrollNamespace] = useState<string | null>(null)
  const [shellWidth, setShellWidth] = useState(900)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [freshnessRevision, setFreshnessRevision] = useState(0)
  const [autoTuned, setAutoTuned] = useState(false)

  const workspaceRef = useRef<HTMLElement>(null)
  const shellRef = useRef<HTMLDivElement>(null)

  /* ---------------------------------------------------------------- 파생 데이터 */

  const nodeById = useMemo(() => new Map((graph?.nodes ?? []).map((node) => [node.id, node])), [graph])
  const classIdOfMethod = useCallback((nodeId: string) => nodeById.get(nodeId)?.classId, [nodeById])
  const namespacePrefix = useMemo(
    () => commonNamespacePrefix((graph?.classes ?? []).map((item) => item.namespace)),
    [graph])
  const relations = useMemo(
    () => (graph ? deriveRelations(graph.classes, collapseEdgesToClasses(graph)) : []),
    [graph])

  const hiddenNamespaces = useMemo(() => new Set(classMap.hiddenNamespaces), [classMap.hiddenNamespaces])
  const diagramOptions: ClassDiagramOptions = useMemo(() => ({
    layout: classMap.layout,
    treeDirection: classMap.treeDirection,
    showFields: classMap.showFields,
    showMethods: classMap.showMethods,
    relations: classMap.relations,
    hiddenNamespaces,
    focusOnly: classMap.focusOnly,
    selection,
    availableWidth: Math.max(360, (shellWidth - 40) / classMap.zoom),
    namespacePrefix,
  }), [classMap, hiddenNamespaces, selection, shellWidth, namespacePrefix])

  const classDiagram = useMemo(
    () => (graph && viewMode === 'classmap'
      ? buildClassDiagram(graph.classes, relations, diagramOptions, classIdOfMethod)
      : null),
    [graph, viewMode, relations, diagramOptions, classIdOfMethod])

  const flowView = useMemo(() => {
    if (!graph || viewMode !== 'flowmap') return null
    return flowEntryId
      ? buildEntryFlow(graph, flowEntryId, depth, entryPoints, namespacePrefix)
      : buildEntryMap(graph, entryPoints, namespacePrefix)
  }, [graph, viewMode, flowEntryId, depth, entryPoints, namespacePrefix])

  const callFlowWindow: GraphWindow | null = useMemo(() => {
    if (!graph || viewMode !== 'callflow' || !callFlowEntry) return null
    const focus = focusStack.length > 0 ? focusStack[focusStack.length - 1] : callFlowEntry.nodeId
    return focusStack.length === 0 ? getInitialWindow(graph, focus) : getDrilldownWindow(graph, focus)
  }, [graph, viewMode, callFlowEntry, focusStack])

  const selectedClassId = selection
    ? selection.type === 'class'
      ? selection.key
      : selection.type === 'method'
        ? classIdOfMethod(selection.key) ?? findClassIdByMemberKey(graph, selection.key)
        : null
    : null

  /* ---------------------------------------------------------------- 상태 저장 */

  useEffect(() => {
    saveState({ viewMode, classMap, depth, selection, columns, solutionPath })
  }, [viewMode, classMap, depth, selection, columns, solutionPath])

  /* ---------------------------------------------------------------- 폭·반응형 */

  const applyColumns = useCallback((next: { left: number; right: number }) => {
    const workspace = workspaceRef.current
    const total = workspace?.clientWidth ?? 1400
    const maxLeft = Math.max(minLeft, total - minCenter - next.right - 16)
    const left = Math.min(Math.max(next.left, minLeft), maxLeft)
    const maxRight = Math.max(minRight, total - minCenter - left - 16)
    const right = Math.min(Math.max(next.right, minRight), maxRight)
    workspace?.style.setProperty('--col-left', `${left}px`)
    workspace?.style.setProperty('--col-right', `${right}px`)
    return { left, right }
  }, [])

  const measureShell = useCallback(() => {
    if (shellRef.current) setShellWidth(shellRef.current.clientWidth)
  }, [])

  useEffect(() => {
    applyColumns(columns)
    measureShell()
    // 창이 줄어들 때도 같은 클램프를 다시 돌리고, 배치는 디바운스해서 다시 계산한다.
    let timer = 0
    const onResize = () => {
      setColumns((current) => applyColumns(current))
      window.clearTimeout(timer)
      timer = window.setTimeout(measureShell, resizeDebounceMs)
    }

    window.addEventListener('resize', onResize)
    return () => {
      window.removeEventListener('resize', onResize)
      window.clearTimeout(timer)
    }
  }, [applyColumns, measureShell]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => { measureShell() }, [viewMode, columns, measureShell])

  /* ---------------------------------------------------------------- 데이터 적재 */

  const loadGraphData = useCallback(async () => {
    const [nextEntryPoints, nextNamespaces, nextGraph] = await Promise.all([
      getEntryPoints(),
      getNamespaces(),
      getGraph(),
    ])
    setEntryPoints(nextEntryPoints)
    setNamespaces(nextNamespaces)
    setGraph(nextGraph)
    return nextGraph
  }, [])

  // 새로고침해도 서버가 들고 있는 마지막 분석 결과로 화면을 되살린다.
  useEffect(() => {
    void loadGraphData().catch(() => undefined)
  }, [loadGraphData])

  /**
   * E-10 대규모 솔루션 기본값. 클래스가 많으면 멤버를 접고 의존을 끈 상태로 시작한다.
   * 사용자가 이미 저장해 둔 설정을 덮어쓰지 않도록 한 번만 적용한다.
   */
  useEffect(() => {
    if (!graph || autoTuned) return
    setAutoTuned(true)
    if (graph.classes.length <= largeSolutionThreshold) return
    setClassMap((current) => ({
      ...current,
      showFields: false,
      showMethods: false,
      relations: { ...current.relations, depend: false },
      zoom: 0.5,
    }))
  }, [graph, autoTuned])

  const analyze = async () => {
    if (!solutionPath.trim()) return
    setBusy(true)
    setError(null)
    try {
      const nextSummary = await analyzeTarget(solutionPath.trim())
      const nextGraph = await loadGraphData()
      setSummary(nextSummary)
      // D-7 재분석 후에도 보고 있던 보기와 선택을 유지한다. 사라진 id만 정리한다.
      setSelection((current) => (current && selectionExists(nextGraph, current) ? current : null))
      setFlowEntryId((current) => (current && nextGraph.nodes.some((node) => node.id === current) ? current : null))
      setCallFlowEntry((current) => {
        if (!current) return null
        return nextGraph.entryPoints.find((item) => item.nodeId === current.nodeId) ?? null
      })
      setFocusStack((current) => current.filter((id) => nextGraph.nodes.some((node) => node.id === id)))
      setFreshnessRevision((value) => value + 1)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '분석에 실패했다.')
    } finally {
      setBusy(false)
    }
  }

  /* ---------------------------------------------------------------- 선택·이동 */

  const scrollToClass = useCallback((classId: string) => {
    const box = classDiagram?.boxes.get(classId)
    if (box) setScrollTarget({ x: box.x, y: box.y, token: Date.now() })
  }, [classDiagram])

  const goToNamespace = useCallback((namespace: string) => {
    setViewMode('classmap')
    setClassMap((current) => ({
      ...current,
      hiddenNamespaces: current.hiddenNamespaces.filter((item) => item !== namespace),
    }))
    setSelection({ type: 'namespace', key: namespace })
    setPendingScrollNamespace(namespace)
  }, [])

  // 네임스페이스를 켜고 나서 다시 배치된 뒤에야 스크롤 좌표를 알 수 있다.
  useEffect(() => {
    if (!pendingScrollNamespace || !classDiagram) return
    const target = classDiagram.packages.find((item) => item.namespace === pendingScrollNamespace)
      ?? [...classDiagram.boxes.values()]
        .filter((box) => classDiagram.visible.find((item) => item.id === box.id)?.namespace === pendingScrollNamespace)
        .sort((left, right) => (left.y - right.y) || (left.x - right.x))[0]
    if (target) setScrollTarget({ x: target.x, y: target.y, token: Date.now() })
    setPendingScrollNamespace(null)
  }, [pendingScrollNamespace, classDiagram])

  const showInDiagram = useCallback((classId: string) => {
    const graphClass = graph?.classes.find((item) => item.id === classId)
    setViewMode('classmap')
    if (graphClass) {
      setClassMap((current) => ({
        ...current,
        hiddenNamespaces: current.hiddenNamespaces.filter((item) => item !== graphClass.namespace),
      }))
    }

    setSelection({ type: 'class', key: classId })
    window.setTimeout(() => scrollToClass(classId), 0)
  }, [graph, scrollToClass])

  /** 어떤 메서드에서 거슬러 올라가 가장 가까운 진입점을 찾는다. */
  const findEntryPointFor = useCallback((nodeId: string): string | null => {
    if (!graph) return null
    const entryIds = new Set(graph.entryPoints.map((item) => item.nodeId))
    const seen = new Set([nodeId])
    const queue = [nodeId]
    while (queue.length > 0) {
      const current = queue.shift()!
      if (entryIds.has(current)) return current
      for (const edge of graph.edges) {
        if (edge.to !== current || seen.has(edge.from)) continue
        seen.add(edge.from)
        queue.push(edge.from)
      }
    }

    return null
  }, [graph])

  const openFlow = useCallback((nodeId: string) => {
    const entryNodeId = findEntryPointFor(nodeId) ?? nodeId
    setViewMode('flowmap')
    setFlowEntryId(entryNodeId)
    setSelection({ type: 'method', key: nodeId })
  }, [findEntryPointFor])

  const detailActions: DetailActions = useMemo(() => ({
    onSelect: setSelection,
    onShowFlow: openFlow,
    onShowInDiagram: showInDiagram,
    onFocusOnly: (classId) => {
      setViewMode('classmap')
      setClassMap((current) => ({ ...current, focusOnly: true }))
      setSelection({ type: 'class', key: classId })
    },
    onGoToNamespace: goToNamespace,
  }), [openFlow, showInDiagram, goToNamespace])

  const selectMember = useCallback((graphClass: GraphClass, member: GraphClassMember) => {
    setSelection({ type: 'method', key: memberKey(graphClass.id, member) })
  }, [])

  const fitZoom = useCallback(() => {
    const shell = shellRef.current
    if (!shell) return
    const view = classDiagram
    if (!view || view.size.width === 0) return
    // 트리는 전체가 들어오게, 패키지 배치는 가장 넓은 패키지가 들어오게 맞춘다.
    const fit = classMap.layout === 'tree'
      ? Math.min(
        (shell.clientWidth - 40) / Math.max(1, view.size.width),
        (shell.clientHeight - 40) / Math.max(1, view.size.height))
      : (shell.clientWidth - 40) / Math.max(400, ...view.packages.map((item) => item.w))
    setClassMap((current) => ({ ...current, zoom: Math.max(0.25, Math.min(1, Number(fit.toFixed(2)))) }))
  }, [classDiagram, classMap.layout])

  const changeMode = (mode: ViewMode) => {
    setViewMode(mode)
    if (mode !== 'classmap') setClassMap((current) => ({ ...current, focusOnly: false }))
    if (mode === 'classmap' && selection?.type === 'namespace') setPendingScrollNamespace(selection.key)
    if (mode === 'callflow' && !callFlowEntry && entryPoints.length > 0) {
      setCallFlowEntry(entryPoints[0])
      setFocusStack([])
    }
  }

  const drilldown = useCallback((nodeId: string) => {
    setFocusStack((current) => (current[current.length - 1] === nodeId ? current : [...current, nodeId]))
  }, [])

  /* ---------------------------------------------------------------- 화면 조각 */

  const breadcrumbs: BreadcrumbItem[] = useMemo(() => {
    if (viewMode === 'callflow') {
      if (!callFlowEntry) return []
      return [
        { key: callFlowEntry.nodeId, label: `${callFlowEntry.httpMethod} ${callFlowEntry.route}` },
        ...focusStack.map((id) => ({ key: id, label: nodeById.get(id)?.displayName ?? id })),
      ]
    }

    if (viewMode === 'classmap') {
      const crumbs: BreadcrumbItem[] = [{ key: 'all', label: '전체 클래스' }]
      if (classMap.focusOnly && selectedClassId) {
        crumbs.push({ key: selectedClassId, label: `${graph?.classes.find((item) => item.id === selectedClassId)?.name ?? selectedClassId} 1홉` })
      } else if (selection?.type === 'namespace') {
        crumbs.push({ key: selection.key, label: shortenNamespace(selection.key, namespacePrefix) })
      }

      return crumbs
    }

    const crumbs: BreadcrumbItem[] = [{ key: 'map', label: '전체 진입점 지도' }]
    if (flowEntryId) {
      const entry = entryPoints.find((item) => item.nodeId === flowEntryId)
      crumbs.push({ key: flowEntryId, label: entry ? `${entry.httpMethod} ${entry.route}` : flowEntryId })
    }

    return crumbs
  }, [viewMode, callFlowEntry, focusStack, nodeById, classMap.focusOnly, selectedClassId, selection, graph, namespacePrefix, flowEntryId, entryPoints])

  const navigateBreadcrumb = (index: number) => {
    if (viewMode === 'callflow') {
      setFocusStack((current) => current.slice(0, index))
      return
    }

    if (viewMode === 'classmap') {
      if (index === 0) setClassMap((current) => ({ ...current, focusOnly: false }))
      return
    }

    if (index === 0) setFlowEntryId(null)
  }

  const askContext: AskContext | null = useMemo(() => {
    if (!graph || !selection) return null
    if (selection.type === 'namespace') {
      const count = graph.classes.filter((item) => item.namespace === selection.key).length
      return {
        nodeId: null,
        lines: [{ label: '네임스페이스', value: selection.key || '(전역)' }, { label: '클래스', value: `${count}개` }],
        clipboardText: `네임스페이스: ${selection.key}\n클래스: ${count}개`,
      }
    }

    if (selection.type === 'class') {
      const item = graph.classes.find((candidate) => candidate.id === selection.key)
      if (!item) return null
      return {
        nodeId: null,
        lines: [
          { label: '파일', value: item.file },
          { label: '라인', value: `${item.startLine}-${item.endLine}` },
          { label: '심볼', value: item.id },
        ],
        clipboardText: `파일: ${item.file}\n라인: ${item.startLine}-${item.endLine}\n심볼: ${item.id}`,
      }
    }

    const node = nodeById.get(selection.key)
    if (node) {
      return {
        nodeId: node.id,
        lines: [
          { label: '파일', value: node.file },
          { label: '라인', value: `${node.startLine}-${node.endLine}` },
          { label: '심볼', value: node.displayName },
        ],
        clipboardText: `파일: ${node.file}\n라인: ${node.startLine}-${node.endLine}\n심볼: ${node.displayName}`,
      }
    }

    // 노드가 없는 멤버(인터페이스 메서드 등)는 선언 줄만 담는다.
    for (const item of graph.classes) {
      const member = item.members.find((candidate) => memberKey(item.id, candidate) === selection.key)
      if (!member) continue
      return {
        nodeId: null,
        lines: [
          { label: '파일', value: item.file },
          { label: '라인', value: `${member.startLine}-${member.endLine}` },
          { label: '심볼', value: `${item.id}.${member.signature ?? member.name}` },
        ],
        clipboardText: `파일: ${item.file}\n라인: ${member.startLine}-${member.endLine}\n심볼: ${item.id}.${member.signature ?? member.name}`,
      }
    }

    return null
  }, [graph, selection, nodeById])

  const staleSchemaNotice = graph && graph.classes.length > 0 &&
    graph.classes.every((item) => item.members.length === 0)
    ? '이 그래프에는 멤버 정보가 없다. 다시 분석해야 클래스 다이어그램을 제대로 그릴 수 있다.'
    : null
  const largeSolutionNotice = graph && graph.classes.length > largeSolutionThreshold
    ? `클래스가 ${graph.classes.length}개다. 왼쪽에서 네임스페이스를 골라 좁혀 보는 편이 빠르다.`
    : null

  const graphMeta = viewMode === 'classmap'
    ? classDiagram
      ? `클래스 ${classDiagram.visible.length}개 · 관계 ${classDiagram.relations.length}개 (상속 ${classDiagram.counts.inherit} · 구현 ${classDiagram.counts.implement} · 연관 ${classDiagram.counts.assoc} · 의존 ${classDiagram.counts.depend})`
      : '솔루션을 분석해야 한다'
    : viewMode === 'flowmap'
      ? flowView?.meta ?? '솔루션을 분석해야 한다'
      : callFlowWindow
        ? `${callFlowWindow.nodes.length} nodes · ${callFlowWindow.edges.length} edges`
        : '진입점을 선택해야 한다'

  return (
    <main>
      <header className="app-header">
        <div>
          <div className="eyebrow">ASP.NET Core call explorer</div>
          <h1>TraceLens</h1>
        </div>
        <FreshnessBadge
          canReanalyze={graph !== null}
          refreshKey={freshnessRevision}
          analyzing={busy}
          onReanalyze={() => analyze()}
        />
        <form onSubmit={(event) => { event.preventDefault(); void analyze() }}>
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

      <ViewToolbar
        viewMode={viewMode}
        onChangeMode={changeMode}
        classMap={classMap}
        onChangeClassMap={(patch) => setClassMap((current) => ({ ...current, ...patch }))}
        onFitZoom={fitZoom}
        callFlowLevel={focusStack.length + 1}
        flowEntryId={flowEntryId}
        depth={depth}
        onChangeDepth={setDepth}
      />

      {error && <div className="global-error">{error}</div>}
      {summary && (
        <div className="analysis-summary">
          진입점 {summary.entryPointCount} · 네임스페이스 {namespaces.length} · 클래스 {summary.classCount}
          (인터페이스 {summary.interfaceCount}) · 멤버 {summary.memberCount} · 메서드 {summary.nodeCount}
          · 호출 엣지 {summary.edgeCount} · {Math.round(summary.graphSizeBytes / 1024)}KB · {summary.elapsedMs}ms
          {summary.previousAnalyzedAt && (
            <> · 재분석 {formatTime(summary.previousAnalyzedAt)} → {formatTime(summary.analyzedAt)}</>
          )}
        </div>
      )}

      <section className="workspace" ref={workspaceRef}>
        <aside className="panel side-panel">
          <header>
            <span>{viewMode === 'classmap' ? '네임스페이스' : '진입점'}</span>
            <small>
              {viewMode === 'classmap'
                ? `${namespaces.length - hiddenNamespaces.size}/${namespaces.length} 표시`
                : `${entryPoints.length}개`}
            </small>
          </header>
          {viewMode === 'callflow' && (
            <EntryPointList
              entryPoints={entryPoints}
              selectedNodeId={callFlowEntry?.nodeId ?? null}
              emptyMessage="솔루션을 분석하면 HTTP 진입점이 표시된다."
              onSelect={(entryPoint) => {
                setCallFlowEntry(entryPoint)
                setFocusStack([])
                setSelection({ type: 'method', key: entryPoint.nodeId })
              }}
            />
          )}
          {viewMode === 'classmap' && (
            <NamespacePanel
              namespaces={namespaces}
              classes={graph?.classes ?? []}
              hiddenNamespaces={hiddenNamespaces}
              namespacePrefix={namespacePrefix}
              selection={selection}
              search={search}
              onSearch={setSearch}
              onToggle={(namespace) => setClassMap((current) => ({
                ...current,
                hiddenNamespaces: current.hiddenNamespaces.includes(namespace)
                  ? current.hiddenNamespaces.filter((item) => item !== namespace)
                  : [...current.hiddenNamespaces, namespace],
              }))}
              onShowAll={() => setClassMap((current) => ({ ...current, hiddenNamespaces: [] }))}
              onHideAll={() => setClassMap((current) => ({
                ...current,
                hiddenNamespaces: namespaces.map((item) => item.namespace),
              }))}
              onGoToNamespace={goToNamespace}
              onGoToClass={(graphClass) => showInDiagram(graphClass.id)}
            />
          )}
          {viewMode === 'flowmap' && (
            <FlowSidePanel
              entryPoints={entryPoints}
              classes={graph?.classes ?? []}
              classIdOfMethod={classIdOfMethod}
              flowEntryId={flowEntryId}
              onSelectMap={() => setFlowEntryId(null)}
              onSelectEntry={(entryPoint) => {
                setFlowEntryId(entryPoint.nodeId)
                setSelection({ type: 'method', key: entryPoint.nodeId })
              }}
            />
          )}
        </aside>

        <Splitter
          side="left"
          onDrag={(delta, start) => setColumns(applyColumns({ left: start.left + delta, right: start.right }))}
          onReset={() => setColumns(applyColumns({ ...columns, left: defaultColumns.left }))}
          columns={columns}
        />

        <section className="panel graph-area">
          <header>
            <span>{viewMode === 'classmap' ? 'Class diagram' : viewMode === 'flowmap' ? 'Flow map' : 'Call flow'}</span>
            <small>{graphMeta}</small>
          </header>
          <Breadcrumb items={breadcrumbs} onNavigate={navigateBreadcrumb} />
          {viewMode === 'classmap' && classDiagram && (
            <ClassDiagram
              view={classDiagram}
              options={diagramOptions}
              selection={selection}
              selectedClassId={selectedClassId}
              selectedMemberKey={selection?.type === 'method' ? selection.key : null}
              zoom={classMap.zoom}
              shellRef={shellRef}
              scrollTarget={scrollTarget}
              notice={staleSchemaNotice ?? largeSolutionNotice}
              onSelectClass={(classId) => setSelection({ type: 'class', key: classId })}
              onSelectMember={selectMember}
            />
          )}
          {viewMode === 'flowmap' && flowView && (
            <FlowDiagram
              view={flowView}
              selection={selection}
              zoom={classMap.zoom}
              shellRef={shellRef}
              onSelectClass={(classId) => setSelection({ type: 'class', key: classId })}
              onSelectRow={(key, rowKind) => {
                setSelection({ type: 'method', key })
                if (rowKind === 'route') setFlowEntryId(key)
              }}
            />
          )}
          {viewMode === 'callflow' && (
            <Suspense fallback={<div className="panel-message">그래프 뷰를 불러오는 중이다.</div>}>
              <GraphView
                graphWindow={callFlowWindow}
                selectedNodeId={selection?.type === 'method' ? selection.key : null}
                onSelectNode={(node) => setSelection({ type: 'method', key: node.id })}
                onDrilldown={(node) => drilldown(node.id)}
              />
            </Suspense>
          )}
          {((viewMode === 'classmap' && !classDiagram) ||
            (viewMode === 'flowmap' && !flowView)) && (
            <div className="graph-shell"><div className="panel-message">솔루션을 분석하면 여기에 그려진다.</div></div>
          )}
        </section>

        <Splitter
          side="right"
          onDrag={(delta, start) => setColumns(applyColumns({ left: start.left, right: start.right - delta }))}
          onReset={() => setColumns(applyColumns({ ...columns, right: defaultColumns.right }))}
          columns={columns}
        />

        <div className="right-column">
          <section className="panel detail-panel">
            <header>
              <span>선택한 항목</span>
              <small>
                {selection?.type === 'class' ? '클래스' : selection?.type === 'method' ? '메서드' : selection?.type === 'namespace' ? '네임스페이스' : ''}
              </small>
            </header>
            <DetailPanel
              graph={graph}
              selection={selection}
              relations={relations}
              namespaces={namespaces}
              entryPoints={entryPoints}
              namespacePrefix={namespacePrefix}
              actions={detailActions}
            />
          </section>
          <AskPanel context={askContext} />
        </div>
      </section>
    </main>
  )
}

function selectionExists(graph: GraphDocument, selection: Selection): boolean {
  if (selection.type === 'class') return graph.classes.some((item) => item.id === selection.key)
  if (selection.type === 'namespace') return graph.classes.some((item) => item.namespace === selection.key)
  return graph.nodes.some((node) => node.id === selection.key) ||
    graph.classes.some((item) => item.members.some((member) => memberKey(item.id, member) === selection.key))
}

/** 노드가 없는 멤버 키에서 그 멤버를 선언한 클래스를 찾는다. */
function findClassIdByMemberKey(graph: GraphDocument | null, key: string): string | null {
  if (!graph) return null
  for (const item of graph.classes) {
    if (item.members.some((member) => memberKey(item.id, member) === key)) return item.id
  }

  return null
}

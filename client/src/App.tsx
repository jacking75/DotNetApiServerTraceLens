import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  analyzeTarget,
  ApiError,
  cancelAnalysisJob,
  getDiagnostics,
  getEntryPoints,
  getGraph,
  getNamespaces,
  adoptImportedGraph,
  importGraph,
  loadSavedGraph,
  getSamples,
  prepareSample,
  startAnalysisJob,
  watchAnalysisJob,
} from './api/client'
import { AskPanel, type AskContext } from './components/AskPanel'
import { Breadcrumb, type BreadcrumbItem } from './components/Breadcrumb'
import { ClassDiagram } from './components/ClassDiagram'
import { DataAccessView } from './components/DataAccessView'
import { DataDetailPanel } from './components/DataDetailPanel'
import { DataSidePanel, MiddlewareSidePanel } from './components/DataSidePanels'
import { DetailPanel, type DetailActions } from './components/DetailPanel'
import { GuidedWalkthrough, type WalkthroughStepId } from './components/GuidedWalkthrough'
import { ImpactPanel } from './components/ImpactPanel'
import { MiddlewareView } from './components/MiddlewareView'
import { FlowDiagram } from './components/FlowDiagram'
import { FreshnessBadge } from './components/FreshnessBadge'
import { PathPicker } from './components/PathPicker'
import { EntryPointList, FlowSidePanel, NamespacePanel, StartupFlowSidePanel } from './components/SidePanels'
import { SnapshotDiffPanel } from './components/SnapshotDiffPanel'
import { Splitter } from './components/Splitter'
import { StartupFlowView } from './components/StartupFlowView'
import { ViewToolbar } from './components/ViewToolbar'
import { WelcomePanel } from './components/WelcomePanel'
import { recipes as allRecipes, type Recipe } from './content/recipes'
import {
  buildClassDiagram,
  commonNamespacePrefix,
  largeSolutionThreshold,
  memberKey,
  shortenNamespace,
  type ClassDiagramOptions,
} from './graph/classDiagram'
import { countBySeverity } from './graph/dataView'
import { buildEntryFlow, buildEntryMap } from './graph/flowMap'
import { collapseEdgesToClasses, deriveRelations } from './graph/relations'
import { findStartupStep } from './graph/startupFlow'
import { getDrilldownWindow, getInitialWindow, getPathWindow } from './graph/windowing'
import { translateError, useI18n, type Language } from './i18n/I18nProvider'
import type { TranslationKey } from './i18n/ko'
import {
  defaultColumns,
  loadState,
  saveState,
  type ClassMapState,
  type DataViewState,
  type MiddlewareViewState,
  type PersistedState,
} from './state/viewState'
import type { DiagnosticsReport, SampleView, SelectionKind } from './types/context'
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

/** "저장" 버튼이 내려받는 파일의 기본 이름. 솔루션 이름 + 분석 시각으로 나중에 알아보기 쉽게 만든다. */
const graphFileName = (graph: GraphDocument) => {
  const baseName = graph.solutionPath.split(/[\\/]/).pop()?.replace(/\.(sln|csproj)$/i, '') || 'tracelens'
  const stamp = graph.analyzedAt.slice(0, 10)
  return `${baseName}.${stamp}.tracelens.json`
}

export default function App() {
  const { t, lang, setLang, formatTime, formatPercent } = useI18n()
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
  const [dataView, setDataView] = useState<DataViewState>(initial.dataView)
  const [middlewareView, setMiddlewareView] = useState<MiddlewareViewState>(initial.middlewareView)
  const [search, setSearch] = useState('')
  const [columns, setColumns] = useState(initial.columns)

  const [callFlowEntry, setCallFlowEntry] = useState<EntryPoint | null>(null)
  const [focusStack, setFocusStack] = useState<string[]>([])
  const [flowEntryId, setFlowEntryId] = useState<string | null>(null)

  const [scrollTarget, setScrollTarget] = useState<{ x: number; y: number; token: number } | null>(null)
  const [pendingScrollNamespace, setPendingScrollNamespace] = useState<string | null>(null)
  const [shellWidth, setShellWidth] = useState(900)
  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState<string | null>(null)
  const [jobId, setJobId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  /** 서버가 500과 함께 준 오류 ID. 로그에서 같은 값을 찾을 수 있다. */
  const [errorId, setErrorId] = useState<string | null>(null)
  const [freshnessRevision, setFreshnessRevision] = useState(0)
  const [autoTuned, setAutoTuned] = useState(false)
  const [guideOpen, setGuideOpen] = useState(false)
  const [walkthroughDone, setWalkthroughDone] = useState<Set<WalkthroughStepId>>(new Set())
  const [diagnostics, setDiagnostics] = useState<DiagnosticsReport | null>(null)
  const [samples, setSamples] = useState<SampleView[]>([])
  const [preparingSample, setPreparingSample] = useState(false)
  /** 예제로 분석했다면 그 예제. 안내에 "이 예제에서 보이는 것"을 함께 띄운다. */
  const [activeSample, setActiveSample] = useState<SampleView | null>(null)
  /** 영향 목록에서 고른 대상까지의 경로. 비어 있으면 평소의 창 규칙을 쓴다. */
  const [highlightPath, setHighlightPath] = useState<string[]>([])
  /** 저장해 둔 보기 위치를 한 번만 되살린다. */
  const [restoredView, setRestoredView] = useState(false)
  /** 가져온 분석의 원본 루트를 고르는 대화상자. */
  const [rootPickerOpen, setRootPickerOpen] = useState(false)
  const [adoptMessage, setAdoptMessage] = useState<string | null>(null)
  const [sampleNotice, setSampleNotice] = useState<string | null>(null)
  const [diffOpen, setDiffOpen] = useState(false)
  const [activeRecipe, setActiveRecipe] = useState<Recipe | null>(null)

  const workspaceRef = useRef<HTMLElement>(null)
  const shellRef = useRef<HTMLDivElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  /* ---------------------------------------------------------------- 파생 데이터 */

  const nodeById = useMemo(() => new Map((graph?.nodes ?? []).map((node) => [node.id, node])), [graph])
  /** 역방향 인접 맵. 진입점 역추적과 경로 찾기가 매번 전체 엣지를 훑지 않게 한다. */
  const incomingByNode = useMemo(() => {
    const map = new Map<string, string[]>()
    for (const edge of graph?.edges ?? []) {
      const list = map.get(edge.to)
      if (list) list.push(edge.from)
      else map.set(edge.to, [edge.from])
    }

    return map
  }, [graph])
  const outgoingByNode = useMemo(() => {
    const map = new Map<string, string[]>()
    for (const edge of graph?.edges ?? []) {
      const list = map.get(edge.from)
      if (list) list.push(edge.to)
      else map.set(edge.from, [edge.to])
    }

    return map
  }, [graph])
  /** from에서 to까지의 가장 짧은 경로(노드 ID 목록). 없으면 빈 배열이다. */
  const findPath = useCallback((from: string, to: string): string[] => {
    if (from === to) return [from]
    const cameFrom = new Map<string, string>()
    const seen = new Set([from])
    const queue = [from]
    while (queue.length > 0) {
      const current = queue.shift()!
      for (const next of outgoingByNode.get(current) ?? []) {
        if (seen.has(next)) continue
        seen.add(next)
        cameFrom.set(next, current)
        if (next === to) {
          const path = [to]
          let cursor = to
          while (cameFrom.has(cursor)) {
            cursor = cameFrom.get(cursor)!
            path.unshift(cursor)
          }

          return path
        }

        queue.push(next)
      }
    }

    return []
  }, [outgoingByNode])
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
    // 영향 목록에서 온 경우에는 대상까지의 경로를 그대로 펼친다(대상이 창 밖에 있으면 이동한 보람이 없다).
    if (highlightPath.length > 0) return getPathWindow(graph, callFlowEntry.nodeId, highlightPath)
    const focus = focusStack.length > 0 ? focusStack[focusStack.length - 1] : callFlowEntry.nodeId
    return focusStack.length === 0 ? getInitialWindow(graph, focus) : getDrilldownWindow(graph, focus)
  }, [graph, viewMode, callFlowEntry, focusStack, highlightPath])

  const dataAccess = graph?.dataAccess ?? null
  const middlewarePipeline = graph?.middleware ?? null
  const findingCounts = useMemo(
    () => countBySeverity([...(dataAccess?.findings ?? []), ...(middlewarePipeline?.findings ?? [])]),
    [dataAccess, middlewarePipeline])
  /** DB·캐시와 미들웨어 보기가 다루는 선택 종류. 상세 패널을 이 기준으로 나눈다. */
  const isDataSelection = selection !== null &&
    (selection.type === 'finding' || selection.type === 'writePath' || selection.type === 'dataSource' ||
      selection.type === 'storageTarget' || selection.type === 'rule' || selection.type === 'middlewareStep')

  const selectedClassId = selection
    ? selection.type === 'class'
      ? selection.key
      : selection.type === 'method'
        ? classIdOfMethod(selection.key) ?? findClassIdByMemberKey(graph, selection.key)
        : null
    : null

  /* ---------------------------------------------------------------- 상태 저장 */

  useEffect(() => {
    saveState({
      viewMode,
      classMap,
      depth,
      selection,
      columns,
      solutionPath,
      dataView,
      middlewareView,
      callFlowEndpointId: callFlowEntry?.endpointId ?? null,
      focusStack,
      flowEntryId,
    })
  }, [
    viewMode, classMap, depth, selection, columns, solutionPath, dataView, middlewareView,
    callFlowEntry, focusStack, flowEntryId,
  ])

  /**
   * 새로고침 뒤 호출 흐름·전체 흐름도의 위치를 되살린다.
   * 저장한 진입점·노드가 지금 그래프에 없으면(재분석으로 사라졌다면) 조용히 버린다.
   */
  useEffect(() => {
    if (!graph || restoredView) return
    setRestoredView(true)
    const entry = initial.callFlowEndpointId
      ? graph.entryPoints.find((item) => item.endpointId === initial.callFlowEndpointId)
      : undefined
    if (entry) {
      setCallFlowEntry(entry)
      const known = new Set(graph.nodes.map((node) => node.id))
      setFocusStack(initial.focusStack.filter((id) => known.has(id)))
    }

    if (initial.flowEntryId && graph.nodes.some((node) => node.id === initial.flowEntryId)) {
      setFlowEntryId(initial.flowEntryId)
    }
  }, [graph, restoredView, initial])

  /* ---------------------------------------------------------------- 첫 사용 안내 진행 */

  const completeStep = useCallback((step: WalkthroughStepId) => {
    setWalkthroughDone((current) => (current.has(step) ? current : new Set(current).add(step)))
  }, [])

  useEffect(() => { if (solutionPath.trim()) completeStep('pick') }, [solutionPath, completeStep])
  useEffect(() => { if (graph) completeStep('analyze') }, [graph, completeStep])
  useEffect(() => { if (callFlowEntry || flowEntryId) completeStep('entry') }, [callFlowEntry, flowEntryId, completeStep])
  useEffect(() => { if (selection) completeStep('evidence') }, [selection, completeStep])

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

  // 폭이 바뀌면 CSS 변수에 반영한다. 리스너 등록과 분리해야 폭을 바꿀 때마다 리스너를 다시 달지 않는다.
  useEffect(() => {
    applyColumns(columns)
    measureShell()
  }, [columns, applyColumns, measureShell])

  // 창이 줄어들 때도 같은 클램프를 다시 돌리고, 배치는 디바운스해서 다시 계산한다.
  useEffect(() => {
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
  }, [applyColumns, measureShell])

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

  /** 분석·불러오기 공통 후처리. 재분석 후에도 보고 있던 보기와 선택을 유지한다(사라진 id만 정리한다). */
  const applyAnalyzedGraph = useCallback((nextGraph: GraphDocument, nextSummary: AnalyzeSummary) => {
    setSummary(nextSummary)
    setSelection((current) => (current && selectionExists(nextGraph, current) ? current : null))
    setFlowEntryId((current) => (current && nextGraph.nodes.some((node) => node.id === current) ? current : null))
    setCallFlowEntry((current) => {
      if (!current) return null
      return nextGraph.entryPoints.find((item) => item.endpointId === current.endpointId) ?? null
    })
    setFocusStack((current) => current.filter((id) => nextGraph.nodes.some((node) => node.id === id)))
    setFreshnessRevision((value) => value + 1)
  }, [])

  // 새로고침해도 서버가 들고 있는 마지막 분석 결과로 화면을 되살린다.
  // 서버가 재시작돼 메모리가 비어 있으면(run.ps1 재실행 등), 기억해 둔 경로의 저장된 분석 결과를 재분석 없이 조용히 불러온다.
  useEffect(() => {
    void (async () => {
      try {
        await loadGraphData()
        return
      } catch {
        // GraphStore가 비어 있다. 아래에서 저장된 파일을 찾아본다.
      }
      // 마운트 시점에 한 번만 도는 effect다. 나중에 바뀐 상태를 읽지 않도록 저장값에서 직접 읽는다.
      const savedPath = initial.solutionPath.trim()
      if (!savedPath) return
      try {
        const nextSummary = await loadSavedGraph(savedPath)
        const nextGraph = await loadGraphData()
        applyAnalyzedGraph(nextGraph, nextSummary)
      } catch (reason) {
        // 저장된 결과가 없으면(첫 실행 등) 조용히 넘어가고, 그 밖의 실패는 사유를 보여 준다.
        if (reason instanceof ApiError && reason.code !== 'NO_ANALYSIS') {
          setError(t('app.restoreFailed', { reason: translateError(t, reason.code, reason.message) }))
        }
      }
    })()
  }, [loadGraphData]) // eslint-disable-line react-hooks/exhaustive-deps

  /**
   * 대규모 솔루션 기본값. 클래스가 많으면 멤버를 접고 의존을 끈 상태로 시작한다.
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
    }))
    // README가 약속한 대로 "맞춤 줌"으로 시작한다. 배치가 끝난 뒤에 재야 해서 다음 프레임에 부른다.
    window.setTimeout(() => fitZoom(), 0)
    // fitZoom은 아래에서 선언한다. 이 effect는 렌더 뒤에 돌아서 그때는 이미 초기화돼 있다.
  }, [graph, autoTuned]) // eslint-disable-line react-hooks/exhaustive-deps

  const describeError = useCallback((reason: unknown, fallback: string) => {
    if (reason instanceof ApiError) {
      const id = (reason.details as { errorId?: string } | undefined)?.errorId ?? null
      setErrorId(id)
      return translateError(t, reason.code, reason.message)
    }

    setErrorId(null)
    return fallback
  }, [t])

  /** 작업 경로로 분석했을 때는 요약을 그래프에서 다시 만든다(서버 응답을 한 번 더 받지 않는다). */
  const summarizeFrom = useCallback(async (nextGraph: GraphDocument): Promise<AnalyzeSummary> => ({
    entryPointCount: nextGraph.entryPoints.length,
    classCount: nextGraph.classes.length,
    interfaceCount: nextGraph.classes.filter((item) => item.typeKind === 'Interface').length,
    nodeCount: nextGraph.nodes.length,
    edgeCount: nextGraph.edges.length,
    memberCount: nextGraph.classes.reduce((total, item) => total + item.members.length, 0),
    graphSizeBytes: 0,
    elapsedMs: 0,
    analyzedAt: nextGraph.analyzedAt,
    previousAnalyzedAt: summary?.analyzedAt ?? null,
    findingCount: (nextGraph.dataAccess?.findings.length ?? 0) + (nextGraph.middleware?.findings.length ?? 0),
    snapshotId: nextGraph.snapshotId,
    callSiteCount: nextGraph.callSites.length,
    resolutionRate: nextGraph.diagnostics?.calls.resolutionRate ?? 0,
    unresolvedCallSites:
      (nextGraph.diagnostics?.calls.ambiguous ?? 0) + (nextGraph.diagnostics?.calls.unresolved ?? 0),
    minimalApiCount: nextGraph.entryPoints.filter((item) => item.source === 'MinimalApi').length,
    origin: 'live',
    notice: nextGraph.diagnostics?.entryPointNotes.join(' ') || null,
  }), [summary])

  /**
   * 분석을 작업으로 실행해서 단계별 진행 상태를 보여 주고 취소도 받는다.
   * 새 그래프로의 전환은 완료 알림을 받은 뒤 여기서 한 번만 한다.
   */
  const analyze = useCallback(async (targetPath?: string) => {
    const target = (targetPath ?? solutionPath).trim()
    if (!target) return
    setBusy(true)
    setError(null)
    setProgress(null)
    try {
      const job = await startAnalysisJob(target)
      setJobId(job.id)
      let failure: string | null = null
      let canceled = false
      await watchAnalysisJob(job.id, (event) => {
        if (event.kind === 'progress') setProgress(`${event.stage} · ${event.message}${event.count === null ? '' : ` (${event.count})`}`)
        if (event.kind === 'error') failure = event.message
        if (event.kind === 'canceled') canceled = true
      })
      if (canceled) return
      if (failure) throw new Error(failure)

      const nextGraph = await loadGraphData()
      applyAnalyzedGraph(nextGraph, await summarizeFrom(nextGraph))
    } catch (reason) {
      // 작업 API 자체가 없는 환경(구버전 서버)에서만 호환 경로로 다시 시도한다.
      // 그 밖의 실패(같은 대상이 이미 돌고 있음·잘못된 경로 등)에 전체 분석을 한 번 더 돌리면 안 된다.
      const missingJobApi = reason instanceof ApiError && (reason.status === 404 || reason.status === 405)
      if (!missingJobApi) {
        setError(describeError(reason, reason instanceof Error ? reason.message : t('app.analyzeFailed')))
        return
      }

      try {
        const nextSummary = await analyzeTarget(target)
        const nextGraph = await loadGraphData()
        applyAnalyzedGraph(nextGraph, nextSummary)
      } catch (fallbackReason) {
        setError(describeError(fallbackReason, reason instanceof Error ? reason.message : t('app.analyzeFailed')))
      }
    } finally {
      setBusy(false)
      setProgress(null)
      setJobId(null)
    }
  }, [solutionPath, loadGraphData, applyAnalyzedGraph, describeError, summarizeFrom, t])


  const cancelAnalysis = async () => {
    if (jobId) await cancelAnalysisJob(jobId)
  }

  /** 지금 분석 결과를 사용자가 고르는 위치에 파일로 저장한다(브라우저의 "다른 이름으로 저장" 창). */
  const saveGraphAs = () => {
    if (!graph) return
    const blob = new Blob([JSON.stringify(graph, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = graphFileName(graph)
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
  }

  /** 사용자가 "불러오기"로 고른 파일을 읽어 서버에 그대로 불러온다(다시 분석하지 않는다). */
  const importFromFile = async (file: File) => {
    setBusy(true)
    setError(null)
    try {
      const text = await file.text()
      const nextSummary = await importGraph(text)
      const nextGraph = await loadGraphData()
      setSolutionPath(nextGraph.solutionPath)
      applyAnalyzedGraph(nextGraph, nextSummary)
    } catch (reason) {
      setError(describeError(reason, t('app.importFailed')))
    } finally {
      setBusy(false)
    }
  }

  /**
   * 가져온 분석에 원본 소스 루트를 연결한다.
   * 입력 지문이 일치할 때만 소스 보기가 열린다(409면 무엇이 다른지 보여 준다).
   */
  const adoptRoot = async (rootDirectory: string) => {
    setRootPickerOpen(false)
    setAdoptMessage(null)
    try {
      await adoptImportedGraph(rootDirectory)
      const nextGraph = await loadGraphData()
      applyAnalyzedGraph(nextGraph, await summarizeFrom(nextGraph))
      setAdoptMessage(t('adopt.done'))
    } catch (reason) {
      if (reason instanceof ApiError && reason.code === 'INPUT_MISMATCH') {
        const changed = (reason.details as { changedFiles?: string[] } | undefined)?.changedFiles ?? []
        setAdoptMessage(`${t('adopt.mismatch')} ${changed.slice(0, 5).join(', ')}`)
        return
      }

      setAdoptMessage(describeError(reason, t('app.importFailed')))
    }
  }

  // 첫 화면의 "예제 분석" 버튼이 쓰는 목록. 실패하면 빈 목록으로 두고 버튼 대신 사유를 보여 준다.
  useEffect(() => {
    void (async () => {
      try {
        setSamples(await getSamples())
      } catch {
        setSamples([])
      }
    })()
  }, [])

  /** 예제를 사용자 영역으로 복사한 뒤 그 경로를 분석 대상으로 삼아 바로 분석한다. */
  const analyzeSample = useCallback(async (sample: SampleView) => {
    setPreparingSample(true)
    setSampleNotice(null)
    try {
      const prepared = await prepareSample(sample.id)
      setSolutionPath(prepared.targetPath)
      setSampleNotice(t(prepared.copied ? 'welcome.sample.copiedTo' : 'welcome.sample.reused', {
        path: prepared.targetPath,
      }))
      setActiveSample(sample)
      await analyze(prepared.targetPath)
    } catch (reason) {
      setError(describeError(reason, t('app.analyzeFailed')))
    } finally {
      setPreparingSample(false)
    }
  }, [analyze, describeError, t])

  const runDiagnostics = async () => {
    try {
      setDiagnostics(await getDiagnostics(solutionPath.trim() || undefined))
    } catch (reason) {
      setError(describeError(reason, t('diagnostics.failed')))
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

  /** 어떤 메서드에서 거슬러 올라가 가장 가까운 진입점을 찾는다. 역방향 인접 맵은 한 번만 만든다. */
  const findEntryPointFor = useCallback((nodeId: string): string | null => {
    if (!graph) return null
    const entryIds = new Set(graph.entryPoints.map((item) => item.nodeId))
    const seen = new Set([nodeId])
    const queue = [nodeId]
    while (queue.length > 0) {
      const current = queue.shift()!
      if (entryIds.has(current)) return current
      for (const from of incomingByNode.get(current) ?? []) {
        if (seen.has(from)) continue
        seen.add(from)
        queue.push(from)
      }
    }

    return null
  }, [graph, incomingByNode])

  const openFlow = useCallback((nodeId: string) => {
    const entryNodeId = findEntryPointFor(nodeId) ?? nodeId
    setViewMode('flowmap')
    setFlowEntryId(entryNodeId)
    setSelection({ type: 'method', key: nodeId })
  }, [findEntryPointFor])

  /**
   * 변경 영향 목록에서 고른 진입점을 기존 호출 흐름 보기로 연다(새 모드를 만들지 않는다).
   * 진입점 → 대상까지의 대표 경로를 찾아 창에 펼치고 그 경로를 강조한다.
   */
  const openEntryPointFromImpact = useCallback((handlerNodeId: string, targetNodeId: string) => {
    const entry = entryPoints.find((item) => item.nodeId === handlerNodeId)
    if (!entry) return
    setViewMode('callflow')
    setCallFlowEntry(entry)
    setFocusStack([])
    setHighlightPath(findPath(handlerNodeId, targetNodeId))
    setSelection({ type: 'method', key: targetNodeId })
  }, [entryPoints, findPath])

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

  const handleZoomChange = useCallback((nextZoom: number) => {
    setClassMap((current) => ({ ...current, zoom: nextZoom }))
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

    if (mode === 'data' && !dataView.entryNodeId && entryPoints.length > 0) {
      setDataView((current) => ({ ...current, entryNodeId: entryPoints[0].nodeId }))
    }
  }

  const startRecipe = (recipe: Recipe) => {
    setActiveRecipe(recipe)
    changeMode(recipe.view)
    if (recipe.id === 'review-ai-change') setDiffOpen(true)
  }

  const drilldown = useCallback((nodeId: string) => {
    setFocusStack((current) => (current[current.length - 1] === nodeId ? current : [...current, nodeId]))
  }, [])

  /* ---------------------------------------------------------------- 화면 조각 */

  const breadcrumbs: BreadcrumbItem[] = useMemo(() => {
    if (viewMode === 'callflow') {
      if (!callFlowEntry) return []
      return [
        { key: callFlowEntry.endpointId, label: `${callFlowEntry.httpMethod} ${callFlowEntry.route}` },
        ...focusStack.map((id) => ({ key: id, label: nodeById.get(id)?.displayName ?? id })),
      ]
    }

    if (viewMode === 'classmap') {
      const crumbs: BreadcrumbItem[] = [{ key: 'all', label: t('view.classmap') }]
      if (classMap.focusOnly && selectedClassId) {
        crumbs.push({
          key: selectedClassId,
          label: `${graph?.classes.find((item) => item.id === selectedClassId)?.name ?? selectedClassId} · 1`,
        })
      } else if (selection?.type === 'namespace') {
        crumbs.push({ key: selection.key, label: shortenNamespace(selection.key, namespacePrefix) })
      }

      return crumbs
    }

    if (viewMode === 'startup') {
      return [{ key: 'startup', label: t('view.startup') }]
    }

    if (viewMode === 'data') {
      const labels: Record<DataViewState['sub'], TranslationKey> = {
        route: 'sub.route',
        map: 'sub.map',
        tx: 'sub.tx',
        comp: 'sub.comp',
        check: 'sub.check',
      }
      const crumbs: BreadcrumbItem[] = [
        { key: 'data', label: t('view.data') },
        { key: dataView.sub, label: t(labels[dataView.sub]) },
      ]
      if (dataView.sub === 'route' && dataView.entryNodeId) {
        const entry = entryPoints.find((item) => item.nodeId === dataView.entryNodeId)
        if (entry) crumbs.push({ key: entry.endpointId, label: `${entry.httpMethod} ${entry.route}` })
      }

      return crumbs
    }

    if (viewMode === 'middleware') {
      const labels: Record<MiddlewareViewState['sub'], TranslationKey> = {
        pipe: 'sub.pipe',
        sc: 'sub.sc',
        check: 'sub.check',
      }
      return [
        { key: 'middleware', label: t('view.middleware') },
        { key: middlewareView.sub, label: t(labels[middlewareView.sub]) },
      ]
    }

    const crumbs: BreadcrumbItem[] = [{ key: 'map', label: t('view.flowmap') }]
    if (flowEntryId) {
      const entry = entryPoints.find((item) => item.nodeId === flowEntryId)
      crumbs.push({ key: flowEntryId, label: entry ? `${entry.httpMethod} ${entry.route}` : flowEntryId })
    }

    return crumbs
  }, [viewMode, callFlowEntry, focusStack, nodeById, classMap.focusOnly, selectedClassId, selection, graph, namespacePrefix, flowEntryId, entryPoints, dataView, middlewareView, t])

  const navigateBreadcrumb = (index: number) => {
    if (viewMode === 'callflow') {
      setFocusStack((current) => current.slice(0, index))
      return
    }

    if (viewMode === 'classmap') {
      if (index === 0) setClassMap((current) => ({ ...current, focusOnly: false }))
      return
    }

    if (viewMode === 'startup' || viewMode === 'data' || viewMode === 'middleware') {
      return
    }

    if (index === 0) setFlowEntryId(null)
  }

  /**
   * 질문 패널에 넘길 선택. 문맥 자체는 서버가 만들고, 여기서는 무엇을 골랐는지만 전달한다.
   * 실행 가능한 노드가 없어도(클래스·네임스페이스) 문맥은 만들 수 있다.
   */
  const askContext: AskContext | null = useMemo(() => {
    if (!graph || !selection) return null
    const line = (label: string, value: string) => ({ label, value })

    const forNode = (nodeId: string): AskContext | null => {
      const node = nodeById.get(nodeId)
      if (!node) return null
      return {
        kind: 'Node' as SelectionKind,
        id: node.id,
        nodeId: node.id,
        lines: [
          line(t('detail.file'), node.file),
          line(t('detail.line'), `${node.startLine}-${node.endLine}`),
          line(t('detail.symbol'), node.displayName),
        ],
      }
    }

    if (selection.type === 'finding') {
      const finding = [...(graph.dataAccess?.findings ?? []), ...(graph.middleware?.findings ?? [])]
        .find((item) => item.id === selection.key)
      if (!finding) return null
      return {
        kind: 'Finding',
        id: finding.id,
        nodeId: finding.nodeId,
        lines: [
          line(t('detail.file'), finding.file),
          line(t('detail.line'), `${finding.line}`),
          line(t('detail.symbol'), finding.symbol),
          line(t('selection.rule'), `${finding.ruleId} · ${finding.what}`),
        ],
      }
    }

    if (selection.type === 'writePath') {
      const path = graph.dataAccess?.writePaths.find((item) => item.id === selection.key)
      if (!path) return null
      return {
        kind: 'WritePath',
        id: path.id,
        nodeId: path.methodNodeId,
        lines: [
          line(t('detail.file'), path.file),
          line(t('detail.line'), `${path.startLine}`),
          line(t('detail.symbol'), path.methodDisplayName),
        ],
      }
    }

    if (selection.type === 'middlewareStep') {
      const step = graph.middleware?.steps.find((item) => item.id === selection.key)
      if (!step) return null
      return {
        kind: 'MiddlewareStep',
        id: step.id,
        nodeId: null,
        lines: [
          line(t('detail.file'), step.implementationFile ?? step.registrationFile),
          line(t('detail.registration'), `${step.registrationFile}:${step.registrationLine}`),
          line(t('detail.symbol'), step.name),
        ],
      }
    }

    if (selection.type === 'namespace') {
      const count = graph.classes.filter((item) => item.namespace === selection.key).length
      return {
        kind: 'Namespace',
        id: selection.key,
        nodeId: null,
        lines: [
          line(t('selection.namespace'), selection.key || '(global)'),
          line(t('selection.class'), t('panel.count', { count })),
        ],
      }
    }

    if (selection.type === 'class') {
      const item = graph.classes.find((candidate) => candidate.id === selection.key)
      if (!item) return null
      return {
        kind: 'Class',
        id: item.id,
        nodeId: null,
        lines: [
          line(t('detail.file'), item.file),
          line(t('detail.line'), `${item.startLine}-${item.endLine}`),
          line(t('detail.symbol'), item.id),
        ],
      }
    }

    if (selection.type === 'startupStep') {
      const step = findStartupStep(graph, selection.key)
      if (!step) return null
      return {
        kind: 'StartupStep',
        id: selection.key,
        nodeId: null,
        lines: [
          line(t('detail.file'), step.file),
          line(t('detail.line'), `${step.line}`),
          line(t('detail.symbol'), step.label),
        ],
      }
    }

    if (selection.type === 'dataSource' || selection.type === 'storageTarget' || selection.type === 'rule') {
      // 이 셋은 아직 서버 문맥 계약에 없다. 가장 가까운 노드가 없으므로 문맥을 만들지 않는다.
      return null
    }

    const node = forNode(selection.key)
    if (node) return node

    // 노드가 없는 멤버(인터페이스 메서드 등)는 그 멤버를 선언한 클래스로 문맥을 만든다.
    for (const item of graph.classes) {
      const member = item.members.find((candidate) => memberKey(item.id, candidate) === selection.key)
      if (!member) continue
      return {
        kind: 'Class',
        id: item.id,
        nodeId: null,
        lines: [
          line(t('detail.file'), item.file),
          line(t('detail.line'), `${member.startLine}-${member.endLine}`),
          line(t('detail.symbol'), `${item.id}.${member.signature ?? member.name}`),
        ],
      }
    }

    return null
  }, [graph, selection, nodeById, t])

  const staleSchemaNotice = graph && graph.classes.length > 0 &&
    graph.classes.every((item) => item.members.length === 0)
    ? t('hint.noMembers')
    : null
  const largeSolutionNotice = graph && graph.classes.length > largeSolutionThreshold
    ? t('hint.manyClasses', { count: graph.classes.length })
    : null

  const graphMeta = viewMode === 'classmap'
    ? classDiagram
      ? t('hint.classCounts', {
          classes: classDiagram.visible.length,
          relations: classDiagram.relations.length,
          inherit: classDiagram.counts.inherit,
          implement: classDiagram.counts.implement,
          assoc: classDiagram.counts.assoc,
          depend: classDiagram.counts.depend,
        })
      : t('empty.needAnalysis')
    : viewMode === 'flowmap'
      ? flowView?.meta ?? t('empty.needAnalysis')
      : viewMode === 'data'
        ? dataAccess
          ? t('hint.dataCounts', {
            accesses: dataAccess.accesses.length,
            paths: dataAccess.writePaths.length,
            findings: dataAccess.findings.length,
          })
          : t('empty.needAnalysis')
      : viewMode === 'middleware'
        ? middlewarePipeline
          ? t('panel.steps', { count: middlewarePipeline.steps.length })
          : t('empty.needAnalysis')
      : viewMode === 'startup'
        ? graph?.startupFlow
          ? t('panel.steps', {
            count: graph.startupFlow.serviceSteps.length + graph.startupFlow.middlewareSteps.length,
          })
          : t('empty.needAnalysis')
        : callFlowWindow
          ? `${callFlowWindow.nodes.length} nodes · ${callFlowWindow.edges.length} edges`
          : t('empty.needEntryPoint')

  const selectedNodeIdForImpact = selection?.type === 'method' ? (nodeById.get(selection.key)?.id ?? null) : null

  return (
    <main>
      <header className="app-header">
        <div>
          <div className="eyebrow">{t('app.eyebrow')}</div>
          <h1>{t('app.title')}</h1>
        </div>
        <FreshnessBadge
          canReanalyze={graph !== null}
          refreshKey={freshnessRevision}
          analyzing={busy}
          onReanalyze={() => analyze()}
        />
        <form onSubmit={(event) => { event.preventDefault(); void analyze() }}>
          <input
            id="tracelens-target-input"
            value={solutionPath}
            onChange={(event) => setSolutionPath(event.target.value)}
            placeholder={t('app.pathPlaceholder')}
            aria-label={t('app.pathLabel')}
          />
          <button type="button" className="browse" onClick={() => setPickerOpen(true)} disabled={busy}>
            {t('app.browse')}
          </button>
          <input
            ref={fileInputRef}
            type="file"
            accept="application/json,.json"
            className="visually-hidden"
            tabIndex={-1}
            aria-label={t('app.import')}
            onChange={(event) => {
              const file = event.target.files?.[0]
              event.target.value = '' // 같은 파일을 다시 골라도 onChange가 뜨도록 비워 둔다.
              if (file) void importFromFile(file)
            }}
          />
          <button
            type="button"
            className="browse"
            title={t('app.importTitle')}
            disabled={busy}
            onClick={() => fileInputRef.current?.click()}
          >
            {t('app.import')}
          </button>
          <button
            type="button"
            className="browse"
            title={t('app.saveTitle')}
            disabled={busy || !graph}
            onClick={saveGraphAs}
          >
            {t('app.save')}
          </button>
          <button id="tracelens-analyze-button" type="submit" className="primary" disabled={busy || !solutionPath.trim()}>
            {busy ? t('app.analyzing') : t('app.analyze')}
          </button>
          {busy && jobId && (
            <button type="button" className="browse" onClick={() => void cancelAnalysis()}>
              {t('app.cancel')}
            </button>
          )}
        </form>
        <div className="header-tools">
          <label className="lang-switch">
            <span className="visually-hidden">{t('lang.label')}</span>
            <select value={lang} onChange={(event) => setLang(event.target.value as Language)} aria-label={t('lang.label')}>
              <option value="ko">{t('lang.ko')}</option>
              <option value="en">{t('lang.en')}</option>
            </select>
          </label>
          <button type="button" className="browse" onClick={() => setGuideOpen((value) => !value)}>
            {guideOpen ? t('app.guideClose') : t('app.guide')}
          </button>
          {graph && (
            <button type="button" className="browse" onClick={() => setDiffOpen((value) => !value)}>
              {t('diff.title')}
            </button>
          )}
        </div>
      </header>

      {pickerOpen && (
        <PathPicker
          initialPath={solutionPath}
          onSelect={(path) => { setSolutionPath(path); setPickerOpen(false) }}
          onClose={() => setPickerOpen(false)}
        />
      )}

      {rootPickerOpen && (
        <PathPicker
          initialPath={solutionPath}
          mode="directory"
          onSelect={(path) => void adoptRoot(path)}
          onClose={() => setRootPickerOpen(false)}
        />
      )}

      {summary?.origin === 'offlineSnapshot' && (
        <div className="notice-banner" role="status">
          <span>{t('adopt.banner')}</span>
          <button type="button" className="link-button" onClick={() => setRootPickerOpen(true)}>
            {t('adopt.cta')}
          </button>
          {adoptMessage && <span className="hint">{adoptMessage}</span>}
        </div>
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
        dataView={dataView}
        onChangeDataView={(patch) => setDataView((current) => ({ ...current, ...patch }))}
        middlewareView={middlewareView}
        onChangeMiddlewareView={(patch) => setMiddlewareView((current) => ({ ...current, ...patch }))}
        shardingEnabled={dataAccess?.shardingEnabled ?? false}
      />

      {error && (
        <div className="global-error" role="alert">
          <span>{error}</span>
          {errorId && <span className="hint">{t('error.errorId', { id: errorId })}</span>}
          <button
            type="button"
            className="link-button"
            onClick={() => { setError(null); setErrorId(null) }}
          >
            {t('error.dismiss')}
          </button>
        </div>
      )}
      {progress && <div className="analysis-progress" aria-live="polite">{progress}</div>}
      {summary?.notice && <div className="analysis-notice">{summary.notice}</div>}
      {summary && (
        <div className="analysis-summary">
          {t('summary.entryPoints', { count: summary.entryPointCount })}
          {summary.minimalApiCount ? ` (${t('summary.minimalApi', { count: summary.minimalApiCount })})` : ''}
          {' · '}{t('summary.namespaces', { count: namespaces.length })}
          {' · '}{t('summary.classes', { count: summary.classCount })}
          {' ('}{t('summary.interfaces', { count: summary.interfaceCount })}{')'}
          {' · '}{t('summary.members', { count: summary.memberCount })}
          {' · '}{t('summary.methods', { count: summary.nodeCount })}
          {' · '}{t('summary.edges', { count: summary.edgeCount })}
          {(findingCounts.Critical + findingCounts.Warning + findingCounts.Info > 0) && (
            <> · {t('summary.findings')} <b className="sev-inline critical">{t('summary.critical', { count: findingCounts.Critical })}</b>
              <b className="sev-inline warning">{t('summary.warning', { count: findingCounts.Warning })}</b>
              <b className="sev-inline info">{t('summary.info', { count: findingCounts.Info })}</b></>
          )}
          {graph?.diagnostics && graph.diagnostics.calls.inspected > 0 && (
            <> · <span title={t('summary.resolutionHint')}>
              {t('summary.resolution', {
                resolved: graph.diagnostics.calls.resolved,
                inspected: graph.diagnostics.calls.inspected,
                rate: formatPercent(graph.diagnostics.calls.resolutionRate),
              })}
            </span></>
          )}
          {(summary.unresolvedCallSites ?? 0) > 0 && (
            <> · {t('summary.unresolved', { count: summary.unresolvedCallSites ?? 0 })}</>
          )}
          {summary.graphSizeBytes > 0 && <> · {Math.round(summary.graphSizeBytes / 1024)}KB</>}
          {summary.elapsedMs > 0 && <> · {summary.elapsedMs}ms</>}
          {summary.previousAnalyzedAt && (
            <> · {t('summary.reanalyzed', {
              from: formatTime(summary.previousAnalyzedAt),
              to: formatTime(summary.analyzedAt),
            })}</>
          )}
        </div>
      )}

      {activeRecipe && (
        <div className="recipe-banner">
          <b>{lang === 'ko' ? activeRecipe.titleKo : activeRecipe.titleEn}</b>
          <ol>
            {(lang === 'ko' ? activeRecipe.stepsKo : activeRecipe.stepsEn).map((step) => <li key={step}>{step}</li>)}
          </ol>
          <div className="hint">
            {t('recipes.prompt')}: “{lang === 'ko' ? activeRecipe.promptKo : activeRecipe.promptEn}”
          </div>
          <div className="hint">
            {t('recipes.verify')}: {lang === 'ko' ? activeRecipe.verifyKo : activeRecipe.verifyEn}
          </div>
          <button type="button" className="link-button" onClick={() => setActiveRecipe(null)}>{t('ask.close')}</button>
        </div>
      )}

      {!graph ? (
        <WelcomePanel
          samples={samples}
          preparingSample={preparingSample}
          sampleNotice={sampleNotice}
          busy={busy}
          diagnostics={diagnostics}
          onAnalyzeSample={(sample) => void analyzeSample(sample)}
          onPickTarget={() => setPickerOpen(true)}
          onRunDiagnostics={() => void runDiagnostics()}
          onStartRecipe={startRecipe}
        />
      ) : (
      <section className="workspace" ref={workspaceRef}>
        <aside className="panel side-panel" id="tracelens-side-panel">
          <header>
            <span>
              {viewMode === 'classmap'
                ? t('panel.namespaces')
                : viewMode === 'startup'
                  ? t('panel.startup')
                  : viewMode === 'data'
                    ? t('panel.data')
                    : viewMode === 'middleware'
                      ? t('panel.pipeline')
                      : t('panel.entryPoints')}
            </span>
            <small>
              {viewMode === 'classmap'
                ? t('panel.visible', { visible: namespaces.length - hiddenNamespaces.size, total: namespaces.length })
                : viewMode === 'startup'
                  ? graph?.startupFlow
                    ? t('panel.steps', {
                      count: graph.startupFlow.serviceSteps.length + graph.startupFlow.middlewareSteps.length,
                    })
                    : ''
                  : viewMode === 'data'
                    ? dataAccess ? t('panel.dataSources', { count: dataAccess.dataSources.length }) : ''
                    : viewMode === 'middleware'
                      ? middlewarePipeline ? t('panel.steps', { count: middlewarePipeline.steps.length }) : ''
                      : t('panel.count', { count: entryPoints.length })}
            </small>
          </header>
          {viewMode === 'callflow' && (
            <EntryPointList
              entryPoints={entryPoints}
              selectedEndpointId={callFlowEntry?.endpointId ?? null}
              emptyMessage={t('empty.entryPoints')}
              classIdOfMethod={classIdOfMethod}
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
          {viewMode === 'startup' && (
            graph?.startupFlow
              ? <StartupFlowSidePanel flow={graph.startupFlow} />
              : <div className="panel-message">{t('empty.startup')}</div>
          )}
          {viewMode === 'data' && (
            dataAccess
              ? (
                <DataSidePanel
                  data={dataAccess}
                  entryPoints={entryPoints}
                  view={dataView}
                  onChangeView={(patch) => setDataView((current) => ({ ...current, ...patch }))}
                  selection={selection}
                  onSelect={setSelection}
                />
              )
              : <div className="panel-message">{t('empty.data')}</div>
          )}
          {viewMode === 'middleware' && (
            middlewarePipeline
              ? (
                <MiddlewareSidePanel
                  pipeline={middlewarePipeline}
                  selection={selection}
                  onSelect={setSelection}
                />
              )
              : <div className="panel-message">{t('empty.middleware')}</div>
          )}
        </aside>

        <Splitter
          side="left"
          onDrag={(delta, start) => setColumns(applyColumns({ left: start.left + delta, right: start.right }))}
          onReset={() => setColumns(applyColumns({ ...columns, left: defaultColumns.left }))}
          columns={columns}
        />

        <section
          className="panel graph-area"
          id="tracelens-view-panel"
          role="tabpanel"
          aria-labelledby={`tracelens-tab-${viewMode}`}
        >
          <header>
            <span>{t(`view.${viewMode}` as const)}</span>
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
              onZoomChange={handleZoomChange}
              onFitZoom={fitZoom}
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
              onZoomChange={handleZoomChange}
            />
          )}
          {viewMode === 'callflow' && (
            <Suspense fallback={<div className="panel-message">{t('empty.loadingGraph')}</div>}>
              <GraphView
                graphWindow={callFlowWindow}
                selectedNodeId={selection?.type === 'method' ? selection.key : null}
                highlightPath={highlightPath}
                onSelectNode={(node) => setSelection({ type: 'method', key: node.id })}
                onDrilldown={(node) => { setHighlightPath([]); drilldown(node.id) }}
              />
            </Suspense>
          )}
          {viewMode === 'startup' && graph?.startupFlow && (
            <StartupFlowView
              flow={graph.startupFlow}
              selection={selection}
              onSelect={(key) => setSelection({ type: 'startupStep', key })}
            />
          )}
          {viewMode === 'data' && graph && dataAccess && (
            <div className="graph-shell" ref={shellRef}>
              <DataAccessView
                graph={graph}
                data={dataAccess}
                entryPoints={entryPoints}
                view={dataView}
                onChangeView={(patch) => setDataView((current) => ({ ...current, ...patch }))}
                selection={selection}
                onSelect={setSelection}
              />
            </div>
          )}
          {viewMode === 'middleware' && middlewarePipeline && (
            <div className="graph-shell">
              <MiddlewareView
                pipeline={middlewarePipeline}
                view={middlewareView}
                selection={selection}
                onSelect={setSelection}
              />
            </div>
          )}
          {((viewMode === 'classmap' && !classDiagram) ||
            (viewMode === 'flowmap' && !flowView) ||
            (viewMode === 'data' && !dataAccess) ||
            (viewMode === 'middleware' && !middlewarePipeline) ||
            (viewMode === 'startup' && !graph?.startupFlow)) && (
            <div className="diagram-wrap"><div className="graph-shell"><div className="panel-message">{t('empty.analyzeFirst')}</div></div></div>
          )}
        </section>

        <Splitter
          side="right"
          onDrag={(delta, start) => setColumns(applyColumns({ left: start.left, right: start.right - delta }))}
          onReset={() => setColumns(applyColumns({ ...columns, right: defaultColumns.right }))}
          columns={columns}
        />

        <div className="right-column">
          {guideOpen && (
            <GuidedWalkthrough
              completed={walkthroughDone}
              expected={activeSample?.expected}
              onClose={() => setGuideOpen(false)}
            />
          )}
          {diffOpen && (
            <SnapshotDiffPanel
              snapshotId={graph?.snapshotId ?? ''}
              analyzing={busy}
              onReanalyze={() => analyze()}
              onClose={() => setDiffOpen(false)}
            />
          )}
          <section className="panel detail-panel" id="tracelens-detail-panel">
            <header>
              <span>{t('panel.selected')}</span>
              <small>
                {selection?.type === 'class'
                  ? t('selection.class')
                  : selection?.type === 'method'
                    ? t('selection.method')
                    : selection?.type === 'namespace'
                      ? t('selection.namespace')
                      : selection?.type === 'startupStep'
                        ? t('selection.startupStep')
                        : selection?.type === 'finding'
                          ? t('selection.finding')
                          : selection?.type === 'writePath'
                            ? t('selection.writePath')
                            : selection?.type === 'dataSource'
                              ? t('selection.dataSource')
                              : selection?.type === 'storageTarget'
                                ? t('selection.storageTarget')
                                : selection?.type === 'rule'
                                  ? t('selection.rule')
                                  : selection?.type === 'middlewareStep'
                                    ? t('selection.middlewareStep')
                                    : ''}
              </small>
            </header>
            {isDataSelection && graph ? (
              <DataDetailPanel
                graph={graph}
                selection={selection!}
                onSelect={setSelection}
                onShowFlow={openFlow}
              />
            ) : (
              <DetailPanel
                graph={graph}
                selection={selection}
                relations={relations}
                namespaces={namespaces}
                entryPoints={entryPoints}
                namespacePrefix={namespacePrefix}
                actions={detailActions}
              />
            )}
          </section>
          <ImpactPanel
            nodeId={selectedNodeIdForImpact}
            snapshotId={graph?.snapshotId ?? ''}
            onOpenEntryPoint={openEntryPointFromImpact}
          />
          <AskPanel
            context={askContext}
            snapshotId={graph?.snapshotId ?? ''}
            onContextCopied={() => completeStep('context')}
          />
        </div>
      </section>
      )}
    </main>
  )
}

function selectionExists(graph: GraphDocument, selection: Selection): boolean {
  if (selection.type === 'finding') {
    return [...(graph.dataAccess?.findings ?? []), ...(graph.middleware?.findings ?? [])]
      .some((item) => item.id === selection.key)
  }

  if (selection.type === 'writePath') return graph.dataAccess?.writePaths.some((item) => item.id === selection.key) ?? false
  if (selection.type === 'dataSource') return graph.dataAccess?.dataSources.some((item) => item.name === selection.key) ?? false
  if (selection.type === 'storageTarget') return graph.dataAccess?.accesses.some((item) => item.target === selection.key) ?? false
  if (selection.type === 'rule') {
    return [...(graph.dataAccess?.rules ?? []), ...(graph.middleware?.rules ?? [])]
      .some((item) => item.id === selection.key)
  }

  if (selection.type === 'middlewareStep') return graph.middleware?.steps.some((item) => item.id === selection.key) ?? false
  if (selection.type === 'class') return graph.classes.some((item) => item.id === selection.key)
  if (selection.type === 'namespace') return graph.classes.some((item) => item.namespace === selection.key)
  if (selection.type === 'startupStep') return findStartupStep(graph, selection.key) !== undefined
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

/** 사용하지 않는 레시피 목록을 참조해서 트리 셰이킹으로 사라지지 않게 한다. */
export const availableRecipes = allRecipes

import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  ApiError,
  getDiagnostics,
  getEntryPoints,
  getGraph,
  getNamespaces,
  adoptImportedGraph,
  importGraph,
  loadSavedGraph,
  openInEditor,
  getSamples,
  prepareSample,
} from './api/client'
import { AskPanel, maxRecentAnswers, type AskContext, type RecentAnswer } from './components/AskPanel'
import { Breadcrumb, type BreadcrumbItem } from './components/Breadcrumb'
import { ClassDiagram } from './components/ClassDiagram'
import { DataAccessView } from './components/DataAccessView'
import { DataDetailPanel } from './components/DataDetailPanel'
import { DataSidePanel, MiddlewareSidePanel } from './components/DataSidePanels'
import { DetailPanel, type DetailActions } from './components/DetailPanel'
import { DiagnosticsCard } from './components/DiagnosticsCard'
import { DiagnosticsPanel } from './components/DiagnosticsPanel'
import { GuidedWalkthrough, type WalkthroughStepId } from './components/GuidedWalkthrough'
import { HelpDialog, HelpLink, type GlossaryTerm, type HelpTab } from './components/HelpDialog'
import { CodeArea } from './components/CodeArea'
import { ImpactPanel, type ImpactTarget } from './components/ImpactPanel'
import { MiddlewareView } from './components/MiddlewareView'
import { FlowDiagram } from './components/FlowDiagram'
import { FreshnessBadge } from './components/FreshnessBadge'
import { HeaderMenu } from './components/HeaderMenu'
import { PathPicker } from './components/PathPicker'
import { QuickFind } from './components/QuickFind'
import type { SourceLocation } from './components/SourceLink'
import { EntryPointList } from './components/EntryPointList'
import { FlowSidePanel, NamespacePanel, StartupFlowSidePanel } from './components/SidePanels'
import { SnapshotDiffPanel } from './components/SnapshotDiffPanel'
import { Splitter } from './components/Splitter'
import { StartupFlowView } from './components/StartupFlowView'
import { ViewToolbar } from './components/ViewToolbar'
import { WelcomePanel } from './components/WelcomePanel'
import { recipes as allRecipes, type Recipe } from './content/recipes'
import { useModal } from './hooks/useModal'
import { shortcutById } from './shortcuts'
import {
  buildClassDiagram,
  commonNamespacePrefix,
  largeSolutionThreshold,
  memberKey,
  shortenNamespace,
  type ClassDiagramOptions,
} from './graph/classDiagram'
import { countBySeverity } from './graph/dataView'
import type { EntryGroupBy, EntrySort } from './graph/entryGroups'
import { buildSvg, toDataUriFile, toSvgFile } from './graph/exportSvg'
import { buildEntryFlow, buildEntryMap, type FlowMapLabels } from './graph/flowMap'
import { collapseEdgesToClasses, deriveRelations } from './graph/relations'
import { findStartupStep } from './graph/startupFlow'
import { translateError, useI18n, type Language } from './i18n/I18nProvider'
import type { TranslationKey } from './i18n/ko'
import {
  addBookmark,
  addRecentTarget,
  defaultColumns,
  fontScales,
  loadState,
  pushRecentFind,
  saveState,
  type Bookmark,
  type ClassMapState,
  type DataViewState,
  type MiddlewareViewState,
  type PersistedState,
  type RightTab,
  type ThemeSetting,
} from './state/viewState'
import { isStructuralChange, parse as parseUrlState, serialize as serializeUrlState } from './state/urlState'
import { useCallFlowState } from './hooks/useCallFlowState'
import { useAnalysisJob } from './hooks/useAnalysisJob'
import type { DiagnosticsReport, ImpactResult, SampleView, SelectionKind } from './types/context'
import type {
  AnalyzeSummary,
  EntryPoint,
  FreshnessResponse,
  GraphClass,
  GraphClassMember,
  GraphDocument,
  GraphNamespaceSummary,
  Selection,
  ViewMode,
} from './types/graph'

const GraphView = lazy(() => import('./components/GraphView').then((module) => ({ default: module.GraphView })))

/** 토스트가 화면에 남아 있는 시간. */
const toastMs = 6_000

const minCenter = 360
const minLeft = 190
const minRight = 280
/** 창 크기가 바뀔 때 배치를 다시 계산하기까지 기다리는 시간. */
const resizeDebounceMs = 120
/** 보기 상태를 localStorage에 쓰기까지 기다리는 시간. 드래그 중에 매 프레임 쓰지 않게 한다. */
const saveDebounceMs = 300

/** 호출자 창을 열 때 진입점을 못 찾았을 때 쓰는 빈 진입점. 브레드크럼 라벨만 쓴다. */
const emptyEntryPoint: EntryPoint = {
  nodeId: '',
  httpMethod: '',
  route: '',
  endpointId: '',
  normalizedRoute: '',
  source: 'Controller',
  handlerKind: 'Method',
  confidence: 'Certain',
  registrationFile: '',
  registrationLine: 0,
  handlerDisplayName: '',
  projectKey: '',
  notes: [],
  groupKey: '',
  hiddenFromApiExplorer: false,
  authorization: null,
}

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
  const [selection, setSelection] = useState<Selection | null>(initial.selection)
  const [dataView, setDataView] = useState<DataViewState>(initial.dataView)
  const [middlewareView, setMiddlewareView] = useState<MiddlewareViewState>(initial.middlewareView)
  const [search, setSearch] = useState('')
  const [columns, setColumns] = useState(initial.columns)

  // 호출 흐름 보기의 상태 한 벌(진입점·파고든 자리·깊이·방향·펼친 묶음·강조 경로)과 거기서 나오는 창.
  const callFlow = useCallFlowState(graph, viewMode, initial.depth)
  const {
    entry: callFlowEntry,
    setEntry: setCallFlowEntry,
    focusStack,
    setFocusStack,
    depth,
    setDepth,
    direction: windowDirection,
    setDirection: setWindowDirection,
    setExpandedGroups,
    highlightPath,
    setHighlightPath,
    window: callFlowWindow,
  } = callFlow
  const [flowEntryId, setFlowEntryId] = useState<string | null>(null)
  /** 진입점 목록의 정렬. 세 보기가 같은 값을 쓴다. */
  const [entrySort, setEntrySort] = useState<EntrySort>(initial.entryListSort)
  /** 우측 열의 탭. 접었다 펴도, 탭을 옮겨도 각 탭의 상태는 남는다. */
  const [rightTab, setRightTab] = useState<RightTab>(initial.rightTab)
  /** 질문 초안·최근 답변·영향 결과는 App이 들고 있어야 우측 열을 접어도 살아남는다. */
  const [askQuestion, setAskQuestion] = useState('')
  const [recentAnswers, setRecentAnswers] = useState<RecentAnswer[]>([])
  const [impactResult, setImpactResult] = useState<ImpactResult | null>(null)
  const [impactTargets, setImpactTargets] = useState<ImpactTarget[]>([])
  /** ★로 찍어 둔 자리. 대상 경로별로 보관한다. */
  const [bookmarks, setBookmarks] = useState<Bookmark[]>(initial.bookmarks)
  const [quickFindRecent, setQuickFindRecent] = useState<string[]>(initial.quickFindRecent)
  /** 시작 흐름·미들웨어에서 고른 실행 프로젝트. 하나뿐이면 화면에 선택이 나오지 않는다. */
  const [projectKey, setProjectKey] = useState<string | null>(initial.projectKey)
  /** 서버에 연결하지 못한 상태. 복구되면 스스로 사라진다. */
  const [serverDown, setServerDown] = useState(false)
  /** 짧게 떴다 사라지는 알림(분석 취소·Markdown 복사 등). */
  const [toast, setToast] = useState<string | null>(null)
  /** 링크가 가리키는 항목이 지금 분석 결과에 없을 때의 안내. */
  const [linkNotice, setLinkNotice] = useState<string | null>(null)
  /** 지금 신선도. 보기 상단의 stale 배너가 쓴다. */
  const [freshness, setFreshness] = useState<FreshnessResponse | null>(null)

  const [scrollTarget, setScrollTarget] = useState<{ x: number; y: number; token: number } | null>(null)
  const [pendingScrollNamespace, setPendingScrollNamespace] = useState<string | null>(null)
  const [shellWidth, setShellWidth] = useState(900)
  /** 분석이 오래 걸릴 때 처방을 띄우기 위한 시작 시각. null이면 분석 중이 아니다. */
  /**
   * 실패 배너. `summary`는 번역한 한 줄, `detail`은 서버·예외 원문(접어 둔다).
   * `code`가 있으면 다음 행동(환경 점검·대상 다시 고르기)을 함께 보여 준다.
   */
  const [error, setError] = useState<{ summary: string; detail: string | null; code: string | null } | null>(null)
  /** 헤더에서 언제든 열 수 있는 환경 점검 모달. 분석에 성공한 뒤에도 닫히지 않는다. */
  const [diagnosticsOpen, setDiagnosticsOpen] = useState(false)
  /** 분석 진단 패널(그래프의 diagnostics). 환경 점검과는 다른 화면이다. */
  const [analysisDiagnosticsOpen, setAnalysisDiagnosticsOpen] = useState(false)
  /** 서버가 500과 함께 준 오류 ID. 로그에서 같은 값을 찾을 수 있다. */
  const [errorId, setErrorId] = useState<string | null>(null)
  const [freshnessRevision, setFreshnessRevision] = useState(0)
  const [autoTuned, setAutoTuned] = useState(false)
  /** 자동 튜닝 직전의 설정. "되돌리기"를 누르면 이 값으로 돌아간다. */
  const [tunedBefore, setTunedBefore] = useState<ClassMapState | null>(null)
  // 첫 방문(저장값에 seen이 없을 때)에는 안내를 열어 둔다.
  const [guideOpen, setGuideOpen] = useState(!initial.guide.seen)
  const [walkthroughDone, setWalkthroughDone] = useState<Set<WalkthroughStepId>>(
    () => new Set(initial.guide.completed))
  /**
   * 복원 직후 첫 렌더에서는 완료 처리를 건너뛴다.
   * localStorage에서 경로·선택이 되살아났다는 이유만으로 "골랐다·확인했다"를 완료로 채우면 진행이 거짓이 된다.
   */
  const guideRestoring = useRef(true)
  /** 대상을 고른 직후. 첫 화면에 다음 행동을 띄우고 분석 버튼을 한 번 강조한다. */
  const [targetPicked, setTargetPicked] = useState(false)
  /** 도움말 대화상자. `?` 링크로 열면 그 용어를 강조한 용어집 탭이 열린다. */
  const [help, setHelp] = useState<{ tab: HelpTab; term: GlossaryTerm | null } | null>(null)
  /** Ctrl+K 통합 찾기. 입력칸에 포커스가 없을 때만 열린다. */
  const [findOpen, setFindOpen] = useState(false)
  /** `파일:줄`을 눌러 연 코드 위치. 상세 패널의 소스 보기가 이 위치를 연다. */
  const [sourceTarget, setSourceTarget] = useState<SourceLocation | null>(null)
  /** 최근에 분석한 대상. 경로 입력칸 옆 드롭다운으로 고른다. */
  const [recentTargets, setRecentTargets] = useState<string[]>(initial.recentTargets)
  const [theme, setTheme] = useState<ThemeSetting>(initial.theme)
  const [fontScale, setFontScale] = useState(initial.fontScale)
  const [collapsed, setCollapsed] = useState(initial.collapsed)
  const openGlossary = useCallback((term: GlossaryTerm) => setHelp({ tab: 'glossary', term }), [])
  const [diagnostics, setDiagnostics] = useState<DiagnosticsReport | null>(null)
  const [samples, setSamples] = useState<SampleView[]>([])
  const [preparingSample, setPreparingSample] = useState(false)
  /** 예제로 분석했다면 그 예제. 안내에 "이 예제에서 보이는 것"을 함께 띄운다. */
  const [activeSample, setActiveSample] = useState<SampleView | null>(null)
  /** 영향 목록에서 고른 대상까지의 경로. 비어 있으면 평소의 창 규칙을 쓴다. */
  /** 저장해 둔 보기 위치를 한 번만 되살린다. */
  const [restoredView, setRestoredView] = useState(false)
  /** 가져온 분석의 원본 루트를 고르는 대화상자. */
  const [rootPickerOpen, setRootPickerOpen] = useState(false)
  const [adoptMessage, setAdoptMessage] = useState<string | null>(null)
  const [sampleNotice, setSampleNotice] = useState<string | null>(null)
  const [diffOpen, setDiffOpen] = useState(false)
  const [activeRecipe, setActiveRecipe] = useState<Recipe | null>(null)
  /** 레시피 요청 예시 복사 결과 문구. */
  const [recipeCopied, setRecipeCopied] = useState<string | null>(null)

  const workspaceRef = useRef<HTMLElement>(null)
  /** applyColumns가 최신 접힘 상태를 읽도록 ref로 들고 있는다(콜백을 다시 만들지 않기 위해서다). */
  const collapsedRef = useRef(initial.collapsed)
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

  /**
   * 선택을 바꾸는 유일한 자리.
   * `파일:줄`로 연 임시 소스와 영향 목록에서 온 경로는 **이전 선택의 것**이라 여기서 함께 비운다.
   * 비우지 않으면 다른 메서드를 골랐는데 이전 메서드의 소스를 보게 된다 — 잘못된 코드를 읽는 버그다.
   */
  const selectItem = useCallback((next: Selection | null) => {
    setSelection(next)
    setSourceTarget(null)
    setHighlightPath([])
  }, [setHighlightPath])

  /** 진입점을 고른다. 호출 흐름의 창을 처음 상태로 되돌린다. */
  const selectEntryPoint = useCallback((entryPoint: EntryPoint) => {
    setCallFlowEntry(entryPoint)
    setFocusStack([])
    setWindowDirection('down')
    setExpandedGroups([])
    selectItem({ type: 'method', key: entryPoint.nodeId })
  }, [selectItem, setCallFlowEntry, setFocusStack, setWindowDirection, setExpandedGroups])
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

  // 흐름도의 문장은 App이 번역해서 넘긴다. graph/는 언어를 모른다.
  const flowLabels = useMemo<FlowMapLabels>(() => ({
    globalNamespace: t('diagram.globalNamespace'),
    memberSummary: (kind, count) => t('flow.memberSummary', { kind, count }),
    entryMapMeta: (entryPoints, classes) => t('flow.entryMapMeta', { entryPoints, classes }),
    entryFlowMeta: (methods, classes, hidden) =>
      t('flow.entryFlowMeta', { methods, classes }) +
      (hidden > 0 ? t('flow.entryFlowHidden', { count: hidden }) : ''),
  }), [t])

  const flowView = useMemo(() => {
    if (!graph || viewMode !== 'flowmap') return null
    return flowEntryId
      ? buildEntryFlow(graph, flowEntryId, depth, entryPoints, namespacePrefix, flowLabels)
      : buildEntryMap(graph, entryPoints, namespacePrefix, flowLabels)
  }, [graph, viewMode, flowEntryId, depth, entryPoints, namespacePrefix, flowLabels])


  /**
   * 실행 프로젝트가 여럿이면 시작 흐름·미들웨어를 골라 본다.
   * 고른 프로젝트가 지금 그래프에 없으면(재분석으로 사라졌다면) 첫 번째로 되돌린다.
   */
  const startupFlows = useMemo(() => graph?.startupFlows ?? [], [graph])
  const middlewarePipelines = useMemo(() => graph?.middlewarePipelines ?? [], [graph])
  const projectKeys = useMemo(() => {
    const keys = new Set<string>()
    for (const item of middlewarePipelines) keys.add(item.projectKey)
    for (const item of startupFlows) keys.add(item.projectKey)
    return [...keys].filter((key) => key.length > 0).sort()
  }, [middlewarePipelines, startupFlows])
  const activeProjectKey = projectKey && projectKeys.includes(projectKey) ? projectKey : projectKeys[0] ?? null
  const startupFlow = (activeProjectKey
    ? startupFlows.find((item) => item.projectKey === activeProjectKey)
    : null) ?? graph?.startupFlow ?? null
  const dataAccess = graph?.dataAccess ?? null
  const middlewarePipeline = (activeProjectKey
    ? middlewarePipelines.find((item) => item.projectKey === activeProjectKey)
    : null) ?? graph?.middleware ?? null
  /** 진입점이 여러 프로젝트에 걸쳐 있으면 목록에 프로젝트 구획을 한 겹 더 둔다. */
  const entryGroupBy: EntryGroupBy = useMemo(
    () => (new Set(entryPoints.map((item) => item.projectKey)).size > 1 ? 'project' : 'controller'),
    [entryPoints])
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

  // 드래그 중에는 저장하지 않는다. pointermove마다 전체 상태를 JSON으로 만들면 프레임이 끊긴다.
  useEffect(() => {
    const timer = window.setTimeout(() => saveState({
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
      guide: { seen: true, completed: [...walkthroughDone] },
      recentTargets,
      theme,
      fontScale,
      collapsed,
      entryListSort: entrySort,
      rightTab,
      bookmarks,
      quickFindRecent,
      projectKey,
    }), saveDebounceMs)
    return () => window.clearTimeout(timer)
  }, [
    viewMode, classMap, depth, selection, columns, solutionPath, dataView, middlewareView,
    callFlowEntry, focusStack, flowEntryId, walkthroughDone, recentTargets, theme, fontScale, collapsed,
    entrySort, rightTab, bookmarks, quickFindRecent, projectKey,
  ])

  /* ---------------------------------------------------------------- 테마·글꼴 */

  /**
   * 테마와 글꼴 배율을 문서 루트에 반영한다.
   * `system`이면 운영체제 설정을 따르고, 그 설정이 바뀌면 함께 바뀐다.
   */
  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: light)')
    const apply = () => {
      const resolved = theme === 'system' ? (media.matches ? 'light' : 'dark') : theme
      document.documentElement.dataset.theme = resolved
    }

    apply()
    media.addEventListener('change', apply)
    return () => media.removeEventListener('change', apply)
  }, [theme])

  useEffect(() => {
    document.documentElement.style.setProperty('--fs-scale', String(fontScale))
  }, [fontScale])

  /** Monaco와 Cytoscape가 읽는 실제 테마. `system`을 그대로 넘기면 안 된다. */
  const resolvedTheme = theme === 'system'
    ? (typeof window !== 'undefined' && window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark')
    : theme

  /* ---------------------------------------------------------------- URL 해시 동기화 */

  /** popstate로 되돌린 직후에는 그 상태를 다시 push하지 않는다. 뒤로 가기가 제자리를 맴돌게 된다. */
  const restoringFromHistory = useRef(false)

  /**
   * 마지막으로 쓴 해시. 선택만 바뀌었으면 `replaceState`로 덮어써서
   * 노드 20개를 눌러 본 뒤 뒤로 가기를 20번 눌러야 하는 상황을 막는다.
   */
  const lastHashState = useRef<ReturnType<typeof parseUrlState> | null>(null)

  useEffect(() => {
    if (!graph) return
    if (restoringFromHistory.current) {
      restoringFromHistory.current = false
      return
    }

    const next = {
      view: viewMode,
      entry: callFlowEntry?.endpointId ?? null,
      focus: focusStack,
      selection,
      sub: viewMode === 'data' ? dataView.sub : viewMode === 'middleware' ? middlewareView.sub : null,
      flowEntry: flowEntryId,
      depth,
      classMap,
      direction: windowDirection,
    }
    const hash = serializeUrlState(next)
    if (hash === window.location.hash) return
    // 보기·진입점·포커스가 바뀌면 히스토리에 쌓고, 선택만 바뀌었으면 덮어쓴다.
    const parsed = parseUrlState(hash)
    const structural = isStructuralChange(lastHashState.current, parsed)
    lastHashState.current = parsed
    if (structural) window.history.pushState(null, '', hash)
    else window.history.replaceState(null, '', hash)
  }, [
    graph, viewMode, callFlowEntry, focusStack, selection, dataView.sub, middlewareView.sub,
    flowEntryId, depth, classMap, windowDirection,
  ])

  /** 해시 한 벌을 화면에 적용한다. 그래프에 없는 ID는 버리고 그 사실을 배너로 알린다. */
  const applyUrlState = useCallback((hash: string, nextEntryPoints: EntryPoint[], nextGraph: GraphDocument) => {
    const parsed = parseUrlState(hash)
    const missing: string[] = []
    restoringFromHistory.current = true
    if (parsed.view) setViewMode(parsed.view)
    if (parsed.entry !== undefined) {
      const entry = nextEntryPoints.find((item) => item.endpointId === parsed.entry)
      if (!entry && parsed.entry) missing.push(parsed.entry)
      setCallFlowEntry(entry ?? null)
    }

    const known = new Set(nextGraph.nodes.map((node) => node.id))
    setFocusStack((parsed.focus ?? []).filter((id) => known.has(id)))
    if (parsed.selection && !selectionExists(nextGraph, parsed.selection)) {
      missing.push(parsed.selection.key)
      setSelection(null)
    } else {
      setSelection(parsed.selection ?? null)
    }

    if (parsed.sub) {
      setDataView((current) => (isDataSub(parsed.sub!) ? { ...current, sub: parsed.sub as DataViewState['sub'] } : current))
      setMiddlewareView((current) => (isMiddlewareSub(parsed.sub!)
        ? { ...current, sub: parsed.sub as MiddlewareViewState['sub'] }
        : current))
    }

    if (parsed.flowEntry !== undefined) {
      const id = parsed.flowEntry
      setFlowEntryId(id && known.has(id) ? id : null)
    }

    if (parsed.depth !== undefined && parsed.depth !== null) setDepth(parsed.depth)
    if (parsed.classMap) setClassMap((current) => ({ ...current, ...parsed.classMap }))
    if (parsed.direction) setWindowDirection(parsed.direction)
    lastHashState.current = parsed
    return missing
  }, [setCallFlowEntry, setFocusStack, setDepth, setWindowDirection])

  useEffect(() => {
    const onPopState = () => {
      if (!graph) return
      applyUrlState(window.location.hash, entryPoints, graph)
    }

    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [entryPoints, graph, applyUrlState])

  /**
   * 새로고침 뒤 호출 흐름·전체 흐름도의 위치를 되살린다.
   * 저장한 진입점·노드가 지금 그래프에 없으면(재분석으로 사라졌다면) 조용히 버린다.
   */
  useEffect(() => {
    if (!graph || restoredView) return
    setRestoredView(true)

    // URL이 localStorage보다 우선한다. 동료가 보낸 링크를 열었는데 내 마지막 화면이 뜨면 링크가 무의미하다.
    if (window.location.hash.length > 1) {
      const missing = applyUrlState(window.location.hash, graph.entryPoints, graph)
      if (missing.length > 0) setLinkNotice(t('url.missingTarget', { id: missing[0] }))
      return
    }

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
  }, [graph, restoredView, initial, applyUrlState, t, setCallFlowEntry, setFocusStack])

  /* ---------------------------------------------------------------- 첫 사용 안내 진행 */

  const completeStep = useCallback((step: WalkthroughStepId) => {
    // 복원 사이클에서는 저장값이 만든 상태를 사용자의 조작으로 오해하지 않는다.
    if (guideRestoring.current) return
    setWalkthroughDone((current) => (current.has(step) ? current : new Set(current).add(step)))
  }, [])

  // 첫 렌더에 딸린 effect가 모두 돈 뒤에 복원 플래그를 내린다.
  useEffect(() => {
    const timer = window.setTimeout(() => { guideRestoring.current = false }, 0)
    return () => window.clearTimeout(timer)
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
    // 접힌 패널은 세로 띠(32px)만 남긴다. 저장한 폭은 그대로 두어 펼치면 되돌아온다.
    workspace?.style.setProperty('--col-left', collapsedRef.current.left ? '32px' : `${left}px`)
    workspace?.style.setProperty('--col-right', collapsedRef.current.right ? '32px' : `${right}px`)
    return { left, right }
  }, [])

  const measureShell = useCallback(() => {
    if (shellRef.current) setShellWidth(shellRef.current.clientWidth)
  }, [])

  // 폭이 바뀌면 CSS 변수에 반영한다. 리스너 등록과 분리해야 폭을 바꿀 때마다 리스너를 다시 달지 않는다.
  useEffect(() => {
    collapsedRef.current = collapsed
    applyColumns(columns)
    measureShell()
  }, [columns, collapsed, applyColumns, measureShell])

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

  // 통합 찾기 단축키. 조건은 `shortcuts.ts` 한 곳에 있어 도움말 표와 어긋날 수 없다.
  useEffect(() => {
    const quickFind = shortcutById('quickFind')
    const onKey = (event: KeyboardEvent) => {
      if (!quickFind?.matches?.(event)) return
      event.preventDefault()
      setFindOpen(true)
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

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
  }, [setCallFlowEntry, setFocusStack])

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
          setError({
            summary: t('app.restoreFailed', { reason: translateError(t, reason.code, reason.message) }),
            detail: reason.message,
            code: reason.code,
          })
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
    setClassMap((current) => {
      setTunedBefore(current)
      return {
        ...current,
        showFields: false,
        showMethods: false,
        relations: { ...current.relations, depend: false },
      }
    })
    // README가 약속한 대로 "맞춤 줌"으로 시작한다. 배치가 끝난 뒤에 재야 해서 다음 프레임에 부른다.
    window.setTimeout(() => fitZoom(), 0)
    // fitZoom은 아래에서 선언한다. 이 effect는 렌더 뒤에 돌아서 그때는 이미 초기화돼 있다.
  }, [graph, autoTuned]) // eslint-disable-line react-hooks/exhaustive-deps

  /** 실패를 배너가 쓸 모양으로 바꾼다. 원문은 버리지 않고 `detail`에 남긴다. */
  const describeError = useCallback((reason: unknown, fallback: string) => {
    if (reason instanceof ApiError) {
      const id = (reason.details as { errorId?: string } | undefined)?.errorId ?? null
      setErrorId(id)
      return {
        summary: translateError(t, reason.code, reason.message),
        detail: reason.message,
        code: reason.code,
      }
    }

    setErrorId(null)
    return {
      summary: fallback,
      detail: reason instanceof Error ? reason.message : null,
      code: null,
    }
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

  // 분석 작업 하나의 수명(시작·진행·취소·실패·재접속)은 훅이 들고 있다.
  const {
    busy, setBusy, progress, jobId, longRunning, analyze, cancelAnalysis,
  } = useAnalysisJob({
    solutionPath,
    loadGraphData,
    applyAnalyzedGraph,
    summarizeFrom,
    describeError,
    setError,
    setErrorId,
    setToast,
    setTargetPicked,
    rememberTarget: (target) => setRecentTargets((current) => addRecentTarget(current, target)),
    setSolutionPath,
    t,
  })

  // 토스트는 몇 초 뒤 스스로 사라진다. 배너 스택을 영구히 차지하면 안 된다.
  useEffect(() => {
    if (!toast) return
    const timer = window.setTimeout(() => setToast(null), toastMs)
    return () => window.clearTimeout(timer)
  }, [toast])

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

      setAdoptMessage(describeError(reason, t('app.importFailed')).summary)
    }
  }

  // 헤더의 버전 표시와 오류 배너의 로그 경로에 쓴다. 실패하면 조용히 넘어간다(분석과 무관하다).
  useEffect(() => {
    void (async () => {
      try {
        setDiagnostics(await getDiagnostics())
      } catch {
        // 서버가 아직 안 떴거나 구버전이다. 버전 표시는 없어도 분석은 그대로 동작한다.
      }
    })()
  }, [])

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

  /** 신선도 확인이 실패하면 서버가 죽은 것이다. 한곳에서 잡아 배너로 알리고 복구되면 지운다. */
  const handleServerReachable = useCallback((reachable: boolean) => setServerDown(!reachable), [])

  const pushRecentAnswer = useCallback((item: RecentAnswer) => {
    setRecentAnswers((list) => [item, ...list].slice(0, maxRecentAnswers))
  }, [])

  /** 목록·표를 Markdown으로 클립보드에 넣는다. PR·회의록에 그대로 붙일 수 있게 한다. */
  const copyMarkdown = useCallback((markdown: string) => {
    void navigator.clipboard.writeText(markdown).then(
      () => setToast(t('export.copied')),
      () => setToast(t('ask.copyFailed')))
  }, [t])

  /**
   * 캔버스를 SVG 한 장으로 저장한다. 지금 필터·토글 상태를 그대로 굳힌다.
   * 회의·PR에 붙일 그림을 얻으려고 화면을 다시 만들지 않아도 되게 하는 것이 목적이다.
   */
  const exportSvg = useCallback((root: HTMLElement, size: { width: number; height: number }) => {
    const background = getComputedStyle(document.documentElement).getPropertyValue('--surface-1').trim() || '#0f1420'
    const name = (graph?.solutionPath.split(/[\\/]/).pop() ?? 'tracelens').replace(/\.(sln|csproj)$/i, '')
    toSvgFile(buildSvg(root, { ...size, background }), `${name}.${viewMode}.svg`)
    setToast(t('export.saveSvg'))
  }, [graph, viewMode, t])

  /** 지금 화면을 가리키는 주소를 복사한다. 루프백이라 외부에 공개되지 않는다(제약 4). */
  const copyShareLink = useCallback(async () => {
    const link = `${window.location.origin}${window.location.pathname}${window.location.hash}`
    try {
      await navigator.clipboard.writeText(link)
      setToast(t('url.copied'))
    } catch {
      setToast(t('ask.copyFailed'))
    }
  }, [t])

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
    selectItem({ type: 'method', key: nodeId })
  }, [findEntryPointFor, selectItem])

  /** 이 메서드를 부르는 쪽으로 자라는 창을 연다. 진입점까지 이어지지 않아도 볼 수 있어야 한다. */
  const showCallerWindow = useCallback((nodeId: string) => {
    const entryNodeId = findEntryPointFor(nodeId)
    const entry = entryNodeId ? entryPoints.find((item) => item.nodeId === entryNodeId) : undefined
    setViewMode('callflow')
    setWindowDirection('up')
    setHighlightPath([])
    setExpandedGroups([])
    // 창의 기준은 고른 노드다. 진입점은 브레드크럼을 위해 함께 둔다.
    setCallFlowEntry(entry ?? { ...(callFlowEntry ?? emptyEntryPoint), nodeId })
    setFocusStack([nodeId])
    setSelection({ type: 'method', key: nodeId })
  }, [findEntryPointFor, entryPoints, callFlowEntry, setCallFlowEntry, setFocusStack, setExpandedGroups, setWindowDirection, setHighlightPath])

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
    setWindowDirection('down')
    setExpandedGroups([])
    setSourceTarget(null)
    setHighlightPath(findPath(handlerNodeId, targetNodeId))
    setSelection({ type: 'method', key: targetNodeId })
  }, [entryPoints, findPath, setCallFlowEntry, setFocusStack, setExpandedGroups, setWindowDirection, setHighlightPath])

  /** 선택한 노드가 지금 창 밖이면 진입점 → 그 노드 경로를 펼쳐 보여 준다. */
  const goToSelectedNode = useCallback(() => {
    if (selection?.type !== 'method' || !callFlowEntry) return
    setWindowDirection('down')
    setFocusStack([])
    setHighlightPath(findPath(callFlowEntry.nodeId, selection.key))
  }, [selection, callFlowEntry, findPath, setFocusStack, setWindowDirection, setHighlightPath])

  /**
   * 에디터에서 연다. 노드 ID 기준 API를 쓰므로 그 위치를 담은 노드를 먼저 찾는다.
   * 찾지 못하면 아무 일도 하지 않는다 — 엉뚱한 파일을 여는 것보다 낫다.
   */
  const openLocationInEditor = useCallback((location: SourceLocation) => {
    const node = (graph?.nodes ?? []).find((item) =>
      item.file === location.file && item.startLine <= location.line && location.line <= item.endLine)
    if (!node) return
    void openInEditor(node.id)
  }, [graph])

  const detailActions: DetailActions = useMemo(() => ({
    onSelect: selectItem,
    onShowFlow: openFlow,
    onShowInDiagram: showInDiagram,
    onFocusOnly: (classId) => {
      setViewMode('classmap')
      setClassMap((current) => ({ ...current, focusOnly: true }))
      setSelection({ type: 'class', key: classId })
    },
    onGoToNamespace: goToNamespace,
    onExplain: openGlossary,
    onOpenSource: setSourceTarget,
    onOpenInEditor: openLocationInEditor,
    onShowCallerWindow: showCallerWindow,
  }), [selectItem, openFlow, showInDiagram, goToNamespace, openGlossary, openLocationInEditor, showCallerWindow])

  const selectMember = useCallback((graphClass: GraphClass, member: GraphClassMember) => {
    selectItem({ type: 'method', key: memberKey(graphClass.id, member) })
  }, [selectItem])

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

  /**
   * 보기를 바꾼다.
   * 보기와 무관한 상태(임시 소스·강조 경로·좌측 검색어)는 여기서 정리한다 —
   * 남겨 두면 다른 보기에서 이전 보기의 경로가 캔버스에 끼어든다.
   */
  const changeMode = (mode: ViewMode) => {
    setViewMode(mode)
    setHighlightPath([])
    setSourceTarget(null)
    setSearch('')
    if (mode !== 'classmap') setClassMap((current) => ({ ...current, focusOnly: false }))
    if (mode === 'classmap' && selection?.type === 'namespace') setPendingScrollNamespace(selection.key)
    if (mode === 'callflow' && !callFlowEntry && entryPoints.length > 0) {
      setCallFlowEntry(entryPoints[0])
      setFocusStack([])
      setWindowDirection('down')
    }

    if (mode === 'data' && !dataView.entryNodeId && entryPoints.length > 0) {
      setDataView((current) => ({ ...current, entryNodeId: entryPoints[0].nodeId }))
    }
  }

  const startRecipe = (recipe: Recipe) => {
    setActiveRecipe(recipe)
    setRecipeCopied(null)
    changeMode(recipe.view)
    if (recipe.id === 'review-ai-change') setDiffOpen(true)
  }

  /** 레시피의 요청 예시를 클립보드에 넣고 질문 패널 입력칸도 함께 채운다. */
  const copyRecipePrompt = async (recipe: Recipe) => {
    const prompt = lang === 'ko' ? recipe.promptKo : recipe.promptEn
    setAskQuestion(prompt)
    setRightTab('ask')
    setCollapsed((current) => ({ ...current, right: false }))
    try {
      await navigator.clipboard.writeText(prompt)
      setRecipeCopied(t('ask.copied'))
    } catch {
      setRecipeCopied(t('ask.copyFailed'))
    }
  }

  /**
   * 통합 찾기 결과를 연다. 진입점 → 호출 흐름, 클래스 → 클래스 다이어그램, 메서드 → 그 노드 선택.
   * 메서드가 어떤 진입점에서도 도달하지 않으면 상세만 열고 그 사실을 상세 패널이 말한다.
   */
  const openSearchHit = useCallback((hit: { kind: string; id: string }) => {
    setFindOpen(false)
    setQuickFindRecent((current) => pushRecentFind(current, `${hit.kind}:${hit.id}`))
    if (hit.kind === 'entryPoint') {
      const entry = entryPoints.find((item) => item.endpointId === hit.id)
      if (!entry) return
      setViewMode('callflow')
      selectEntryPoint(entry)
      return
    }

    if (hit.kind === 'class') {
      showInDiagram(hit.id)
      return
    }

    if (hit.kind === 'rule') {
      setViewMode('data')
      selectItem({ type: 'rule', key: hit.id })
      return
    }

    if (hit.kind === 'finding') {
      setViewMode('data')
      selectItem({ type: 'finding', key: hit.id })
      return
    }

    if (hit.kind === 'file') {
      setSourceTarget({ file: hit.id, line: 1 })
      return
    }

    // 메서드: 도달하는 진입점이 있으면 그 호출 흐름을 열고, 없으면 선택만 한다.
    const entryNodeId = findEntryPointFor(hit.id)
    const entry = entryNodeId ? entryPoints.find((item) => item.nodeId === entryNodeId) : undefined
    setSourceTarget(null)
    if (entry) {
      setViewMode('callflow')
      setCallFlowEntry(entry)
      setFocusStack([])
      setWindowDirection('down')
      setExpandedGroups([])
      setHighlightPath(findPath(entry.nodeId, hit.id))
    } else {
      setHighlightPath([])
    }

    setSelection({ type: 'method', key: hit.id })
  }, [entryPoints, findEntryPointFor, findPath, showInDiagram, selectEntryPoint, selectItem, setCallFlowEntry, setFocusStack, setExpandedGroups, setWindowDirection, setHighlightPath])

  /** 지금 선택의 이름. 북마크 목록과 영향 결과 머리에 쓴다. */
  const selectionLabel = useMemo(() => {
    if (!selection) return ''
    if (selection.type === 'method') return nodeById.get(selection.key)?.displayName ?? selection.key
    return selection.key
  }, [selection, nodeById])

  const bookmarkTarget = solutionPath.trim().toLowerCase()
  const isBookmarked = selection !== null && bookmarks.some((item) =>
    item.target === bookmarkTarget && item.selection.type === selection.type && item.selection.key === selection.key)

  const toggleBookmark = useCallback(() => {
    if (!selection) return
    setBookmarks((current) => {
      const exists = current.some((item) =>
        item.target === bookmarkTarget &&
        item.selection.type === selection.type &&
        item.selection.key === selection.key)
      if (exists) {
        return current.filter((item) => !(item.target === bookmarkTarget &&
          item.selection.type === selection.type &&
          item.selection.key === selection.key))
      }

      return addBookmark(current, {
        target: bookmarkTarget,
        selection,
        label: selectionLabel,
        at: new Date().toISOString(),
      })
    })
  }, [selection, bookmarkTarget, selectionLabel])

  const drilldown = useCallback((nodeId: string) => {
    setFocusStack((current) => (current[current.length - 1] === nodeId ? current : [...current, nodeId]))
  }, [setFocusStack])

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
      // 포커스나 네임스페이스 선택이 있을 때만 "전체"로 돌아갈 자리가 있다.
      const canReset = classMap.focusOnly || selection?.type === 'namespace'
      const crumbs: BreadcrumbItem[] = [{ key: 'all', label: t('view.classmap'), navigable: canReset }]
      if (classMap.focusOnly && selectedClassId) {
        crumbs.push({
          key: selectedClassId,
          label: `${graph?.classes.find((item) => item.id === selectedClassId)?.name ?? selectedClassId} · 1`,
        })
      } else if (selection?.type === 'namespace') {
        crumbs.push({
          key: selection.key,
          label: shortenNamespace(selection.key, namespacePrefix, t('diagram.globalNamespace')),
        })
      }

      return crumbs
    }

    if (viewMode === 'startup') {
      // 단계를 고른 상태에서만 "선택 해제"로 되돌아갈 자리가 있다.
      return [{ key: 'startup', label: t('view.startup'), navigable: selection?.type === 'startupStep' }]
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
        { key: 'data', label: t('view.data'), navigable: dataView.pathId !== null || dataView.entryNodeId !== null },
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
        { key: 'middleware', label: t('view.middleware'), navigable: middlewareView.sub !== 'pipe' },
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
      if (index === 0) {
        setClassMap((current) => ({ ...current, focusOnly: false }))
        setSelection((current) => (current?.type === 'namespace' ? null : current))
      }

      return
    }

    if (viewMode === 'startup') {
      if (index === 0) setSelection(null)
      return
    }

    if (viewMode === 'data') {
      // 첫 항목은 실행 경로 선택과 펼친 쓰기 경로를 함께 푼다. 두 번째는 하위 보기 자신이라 비활성이다.
      if (index === 0) setDataView((current) => ({ ...current, entryNodeId: null, pathId: null }))
      return
    }

    if (viewMode === 'middleware') {
      if (index === 0) setMiddlewareView((current) => ({ ...current, sub: 'pipe' }))
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
  /** 자동 튜닝이 일어났다면 무엇을 바꿨는지 알리고 되돌릴 수 있게 한다. */
  const autoTuneNotice = tunedBefore && graph
    ? t('hint.autoTuned', { count: graph.classes.length })
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
        ? startupFlow
          ? t('panel.steps', {
            count: startupFlow.serviceSteps.length + startupFlow.middlewareSteps.length,
          })
          : t('empty.needAnalysis')
        : callFlowWindow
          ? t('graph.summary', { nodes: callFlowWindow.nodes.length, edges: callFlowWindow.edges.length })
          : t('empty.needEntryPoint')

  const selectedNodeIdForImpact = selection?.type === 'method' ? (nodeById.get(selection.key)?.id ?? null) : null
  /** HTTP 밖 실행 단위 수. 진입점 목록이 전부가 아니라는 것을 그 자리에서 말한다. */
  const nonHttpUnitCount = (graph?.diagnostics?.entryPointCandidates ?? [])
    .reduce((total, group) => total + group.count, 0)

  /**
   * 빈 보기 문구. 분석 전에는 "분석하면 보인다", 분석 뒤에는 "찾지 못했다 + 다음 행동"으로 나눈다.
   * 이미 분석한 사람에게 "솔루션을 분석하면 표시된다"는 실패로 읽힌다.
   */
  const emptyMessage = (beforeKey: TranslationKey, afterKey: TranslationKey) =>
    graph ? t(afterKey) : t(beforeKey)

  /** 분석 뒤 빈 보기에는 진단으로 가는 버튼을 붙인다. */
  const emptyBlock = (beforeKey: TranslationKey, afterKey: TranslationKey) => (
    <div className="panel-message">
      {emptyMessage(beforeKey, afterKey)}
      {graph && (
        <>
          {' '}
          <button type="button" className="link-button" onClick={() => setAnalysisDiagnosticsOpen(true)}>
            {t('hint.seeDiagnostics')}
          </button>
        </>
      )}
    </div>
  )

  return (
    <main>
      <header className="app-header">
        <div className="app-brand">
          <div className="eyebrow">{t('app.eyebrow')}</div>
          <h1>{t('app.title')}</h1>
        </div>
        <FreshnessBadge
          canReanalyze={graph !== null}
          refreshKey={freshnessRevision}
          analyzing={busy}
          onReanalyze={() => analyze()}
          onExplain={openGlossary}
          onServerReachable={handleServerReachable}
          onFreshness={setFreshness}
          onOpenSource={setSourceTarget}
          onOpenInEditor={openLocationInEditor}
        />
        <form onSubmit={(event) => { event.preventDefault(); void analyze() }}>
          <input
            id="tracelens-target-input"
            disabled={busy}
            value={solutionPath}
            onChange={(event) => setSolutionPath(event.target.value)}
            placeholder={t('app.pathPlaceholder')}
            aria-label={t('app.pathLabel')}
          />
          {recentTargets.length > 0 && (
            <select
              className="recent-targets"
              disabled={busy}
              value=""
              aria-label={t('app.recentTargets')}
              title={t('app.recentTargets')}
              onChange={(event) => {
                if (!event.target.value) return
                setSolutionPath(event.target.value)
                setTargetPicked(true)
              }}
            >
              <option value="">{t('app.recentTargets')}</option>
              {recentTargets.map((path) => (
                <option key={path} value={path}>{path}</option>
              ))}
            </select>
          )}
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
            id="tracelens-analyze-button"
            type="submit"
            className={targetPicked ? 'primary tracelens-attention' : 'primary'}
            disabled={busy || !solutionPath.trim()}
          >
            {busy ? t('app.analyzing') : t('app.analyze')}
          </button>
          {busy && jobId && (
            <button type="button" className="browse" onClick={() => void cancelAnalysis()}>
              {t('app.cancel')}
            </button>
          )}
        </form>
        <div className="header-tools">
          <HeaderMenu id="tracelens-menu-result" label={t('menu.result')}>
            <button
              type="button"
              title={t('app.importTitle')}
              disabled={busy}
              onClick={() => fileInputRef.current?.click()}
            >
              {t('app.import')}
            </button>
            <button type="button" title={t('app.saveTitle')} disabled={busy || !graph} onClick={saveGraphAs}>
              {t('app.save')}
            </button>
            <button type="button" disabled={!graph} onClick={() => { setDiffOpen(true); setCollapsed((c) => ({ ...c, right: false })) }}>
              {t('diff.title')}
            </button>
            <button type="button" disabled={!graph} onClick={() => setAnalysisDiagnosticsOpen(true)}>
              {t('diagPanel.open')}
            </button>
            <button type="button" disabled={!graph} onClick={() => void copyShareLink()}>
              {t('url.copyLink')}
            </button>
          </HeaderMenu>
          <HeaderMenu id="tracelens-menu-help" label={t('menu.help')}>
            <button type="button" onClick={() => setGuideOpen((value) => !value)}>
              {guideOpen ? t('app.guideClose') : walkthroughDone.size > 0 ? t('guide.reopen') : t('app.guide')}
            </button>
            <button type="button" onClick={() => setHelp({ tab: 'glossary', term: null })}>{t('help.glossary')}</button>
            <button type="button" onClick={() => setHelp({ tab: 'shortcuts', term: null })}>{t('help.tab.shortcuts')}</button>
            <a href={`/manual/${lang}`} target="_blank" rel="noreferrer">{t('help.manual')}</a>
            <button type="button" onClick={() => { setDiagnosticsOpen(true); void runDiagnostics() }}>
              {t('diagnostics.open')}
            </button>
          </HeaderMenu>
          <HeaderMenu id="tracelens-menu-settings" label={t('menu.settings')}>
            <div className="font-scale" role="group" aria-label={t('view.fontScale')}>
              {fontScales.map((scale, index) => (
                <button
                  key={scale}
                  type="button"
                  className={fontScale === scale ? 'chipbtn active' : 'chipbtn'}
                  title={t('view.fontScale')}
                  onClick={() => setFontScale(scale)}
                >
                  {index === 0 ? 'A-' : index === 1 ? 'A' : 'A+'}
                </button>
              ))}
            </div>
            <button
              type="button"
              title={t('view.themeTitle')}
              onClick={() => setTheme(resolvedTheme === 'light' ? 'dark' : 'light')}
            >
              {resolvedTheme === 'light' ? `🌙 ${t('view.themeDark')}` : `☀ ${t('view.themeLight')}`}
            </button>
            <label className="lang-switch">
              <span className="visually-hidden">{t('lang.label')}</span>
              <select value={lang} onChange={(event) => setLang(event.target.value as Language)} aria-label={t('lang.label')}>
                <option value="ko">{t('lang.ko')}</option>
                <option value="en">{t('lang.en')}</option>
              </select>
            </label>
            {diagnostics && (
              <span className="app-version" title={`${t('diagnostics.title')}: ${diagnostics.runtimeVersion}`}>
                {diagnostics.toolVersion}
              </span>
            )}
          </HeaderMenu>
        </div>
      </header>

      {pickerOpen && (
        <PathPicker
          initialPath={solutionPath || recentTargets[0] || ''}
          onSelect={(path) => {
            setSolutionPath(path)
            setPickerOpen(false)
            // 자동 분석은 하지 않는다(재분석은 항상 사용자가 실행한다). 대신 다음 행동을 화면에 띄운다.
            setTargetPicked(true)
          }}
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

      <div className="banner-stack">
      {serverDown && (
        <div className="global-error" role="alert">
          <div className="error-head">
            <span>{t('error.serverDown')}</span>
            <button type="button" className="link-button" onClick={() => setFreshnessRevision((value) => value + 1)}>
              {t('error.retry')}
            </button>
          </div>
        </div>
      )}
      {linkNotice && (
        <div className="notice-banner" role="status">
          <span>{linkNotice}</span>
          <button type="button" className="link-button" onClick={() => setLinkNotice(null)}>{t('error.dismiss')}</button>
        </div>
      )}
      {toast && (
        <div className="notice-banner" role="status">
          <span>{toast}</span>
          <button type="button" className="link-button" onClick={() => setToast(null)}>{t('error.dismiss')}</button>
        </div>
      )}
      {graph && freshness?.status === 'stale' && (
        <div className="analysis-notice warn-banner" role="status">
          <span aria-hidden="true">⚠</span>
          <b>{t('freshness.staleBanner', { count: freshness.changedFiles.length })}</b>
          <button type="button" className="link-button" onClick={() => void analyze()} disabled={busy}>
            {t('freshness.reanalyze')}
          </button>
        </div>
      )}
      {error && (
        <div className="global-error" role="alert">
          <div className="error-head">
            <span>{error.summary}</span>
            <button
              type="button"
              className="link-button"
              onClick={() => { setDiagnosticsOpen(true); void runDiagnostics() }}
            >
              {t('diagnostics.open')}
            </button>
            <button type="button" className="link-button" onClick={() => setPickerOpen(true)}>
              {t('error.pickTargetAgain')}
            </button>
            <button
              type="button"
              className="link-button"
              onClick={() => { setError(null); setErrorId(null) }}
            >
              {t('error.dismiss')}
            </button>
          </div>
          {error.detail && error.detail !== error.summary && (
            <details>
              <summary>{t('error.showRaw')}</summary>
              <pre>{error.detail}</pre>
            </details>
          )}
          {errorId && (
            <span className="hint">
              {t('error.errorId', { id: errorId })}
              {diagnostics?.log?.logFilePath && ` · ${t('diagnostics.logFile')}: ${diagnostics.log.logFilePath}`}
            </span>
          )}
        </div>
      )}
      {progress && (
        <div className="analysis-progress" aria-live="polite">
          {progress}
          {longRunning && (
            <span className="hint">
              {' · '}{t('app.longRunningHint')}
              <a href={`/manual/${lang}#target-config`} target="_blank" rel="noreferrer">{t('help.manual')}</a>
            </span>
          )}
        </div>
      )}
      {summary?.notice && (
        <div className="analysis-notice warn-banner" role="status">
          <span aria-hidden="true">⚠</span>
          <b>{summary.notice}</b>
          <button type="button" className="link-button" onClick={() => setAnalysisDiagnosticsOpen(true)}>
            {t('hint.seeDiagnostics')}
          </button>
        </div>
      )}
      {summary && (
        <div className="analysis-summary">
          {t('summary.entryPoints', { count: summary.entryPointCount })}
          {` (${t('summary.minimalApi', { count: summary.minimalApiCount ?? 0 })})`}
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
              {t('summary.resolutionWithExternal', {
                resolved: graph.diagnostics.calls.resolved,
                inspected: graph.diagnostics.calls.inspected,
                rate: formatPercent(graph.diagnostics.calls.resolutionRate),
                external: graph.diagnostics.calls.external,
              })}
            </span><HelpLink term="resolutionRate" onOpen={openGlossary} /></>
          )}
          {(summary.unresolvedCallSites ?? 0) > 0 && (
            <> · {t('summary.unresolved', { count: summary.unresolvedCallSites ?? 0 })}</>
          )}
          {summary.graphSizeBytes > 0 && <> · {Math.round(summary.graphSizeBytes / 1024)}KB</>}
          {summary.elapsedMs > 0 && <> · {summary.elapsedMs}ms</>}
          {' · '}
          <button type="button" className="link-button" onClick={() => setAnalysisDiagnosticsOpen(true)}>
            {t('diagPanel.open')}
          </button>
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
          <div className="row">
            <button
              type="button"
              className="link-button"
              onClick={() => void copyRecipePrompt(activeRecipe)}
            >
              {t('recipes.copyPrompt')}
            </button>
            <a
              className="link-button"
              href={`/manual/${lang}#${activeRecipe.manualAnchor}`}
              target="_blank"
              rel="noreferrer"
            >
              {t('help.openInManual')}
            </a>
            <button type="button" className="link-button" onClick={() => setActiveRecipe(null)}>{t('ask.close')}</button>
          </div>
          {recipeCopied && <div className="hint">{recipeCopied}</div>}
        </div>
      )}
      </div>

      {analysisDiagnosticsOpen && (
        <DiagnosticsPanel
          diagnostics={graph?.diagnostics ?? null}
          dataAccessNotes={graph?.dataAccess?.notes ?? []}
          manifest={graph?.manifest ?? null}
          minimalApiCount={summary?.minimalApiCount ?? 0}
          entryPointCount={entryPoints.length}
          onClose={() => setAnalysisDiagnosticsOpen(false)}
        />
      )}

      {diagnosticsOpen && (
        <EnvironmentDialog
          report={diagnostics}
          errorId={errorId}
          onClose={() => setDiagnosticsOpen(false)}
        />
      )}

      {findOpen && (
        <QuickFind
          graph={graph}
          recent={quickFindRecent}
          bookmarks={bookmarks.filter((item) => item.target === bookmarkTarget)}
          onPickBookmark={(bookmark) => { setFindOpen(false); selectItem(bookmark.selection) }}
          onPick={openSearchHit}
          onClose={() => setFindOpen(false)}
        />
      )}

      {help && (
        <HelpDialog
          initialTab={help.tab}
          focusTerm={help.term}
          analyzed={graph !== null}
          onStartRecipe={startRecipe}
          onClose={() => setHelp(null)}
        />
      )}

      {/* 안내는 workspace 밖에 둔다. 가장 안내가 필요한 순간(분석 전)에도 보여야 한다. */}
      {guideOpen && (
        <div className="walkthrough-dock">
          <GuidedWalkthrough
            completed={walkthroughDone}
            expected={activeSample?.expected}
            onStartRecipe={startRecipe}
            onOpenImpact={() => {
              changeMode('callflow')
              setCollapsed((current) => ({ ...current, right: false }))
              setRightTab('impact')
              window.setTimeout(
                () => document.getElementById('tracelens-impact-panel')?.scrollIntoView({ block: 'nearest' }),
                0)
            }}
            onOpenDiff={() => { setCollapsed((current) => ({ ...current, right: false })); setDiffOpen(true) }}
            onClose={() => setGuideOpen(false)}
          />
        </div>
      )}

      {!graph ? (
        <WelcomePanel
          samples={samples}
          preparingSample={preparingSample}
          sampleNotice={sampleNotice}
          busy={busy}
          diagnostics={diagnostics}
          targetPicked={targetPicked}
          onAnalyzeSample={(sample) => void analyzeSample(sample)}
          onPickTarget={() => setPickerOpen(true)}
          onRunDiagnostics={() => void runDiagnostics()}
          onStartRecipe={startRecipe}
        />
      ) : (
      <section className="workspace" ref={workspaceRef}>
        <aside className={`panel side-panel${collapsed.left ? ' collapsed' : ''}`} id="tracelens-side-panel">
          <header>
            <button
              type="button"
              className="panel-collapse"
              aria-expanded={!collapsed.left}
              title={collapsed.left ? t('view.expandPanel') : t('view.collapsePanel')}
              onClick={() => setCollapsed((current) => ({ ...current, left: !current.left }))}
            >
              {collapsed.left ? '›' : '‹'}
            </button>
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
                  ? startupFlow
                    ? t('panel.steps', {
                      count: startupFlow.serviceSteps.length + startupFlow.middlewareSteps.length,
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
              emptyMessage={emptyMessage('empty.entryPoints', 'empty.noEntryPointsFound')}
              candidateCount={nonHttpUnitCount}
              onShowDiagnostics={() => setAnalysisDiagnosticsOpen(true)}
              classIdOfMethod={classIdOfMethod}
              sort={entrySort}
              onChangeSort={setEntrySort}
              groupBy={entryGroupBy}
              onSelect={(entryPoint) => {
                selectEntryPoint(entryPoint)
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
              onSelectMember={selectMember}
            />
          )}
          {viewMode === 'flowmap' && (
            <FlowSidePanel
              entryPoints={entryPoints}
              classIdOfMethod={classIdOfMethod}
              flowEntryId={flowEntryId}
              sort={entrySort}
              onChangeSort={setEntrySort}
              groupBy={entryGroupBy}
              onSelectMap={() => setFlowEntryId(null)}
              onSelectEntry={(entryPoint) => {
                setFlowEntryId(entryPoint.nodeId)
                selectItem({ type: 'method', key: entryPoint.nodeId })
              }}
            />
          )}
          {viewMode === 'startup' && (
            startupFlow
              ? <StartupFlowSidePanel flow={startupFlow} />
              : emptyBlock('empty.startup', 'empty.noStartupFound')
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
                  onSelect={selectItem}
                  classIdOfMethod={classIdOfMethod}
                  entrySort={entrySort}
                  onChangeEntrySort={setEntrySort}
                  entryGroupBy={entryGroupBy}
                />
              )
              : emptyBlock('empty.data', 'empty.noDataAccessFound')
          )}
          {viewMode === 'middleware' && (
            middlewarePipeline
              ? (
                <MiddlewareSidePanel
                  pipeline={middlewarePipeline}
                  selection={selection}
                  onSelect={selectItem}
                />
              )
              : emptyBlock('empty.middleware', 'empty.noMiddlewareFound')
          )}
        </aside>

        <Splitter
          side="left"
          onPreview={(delta, start) => applyColumns({ left: start.left + delta, right: start.right })}
          onCommit={setColumns}
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
              notice={staleSchemaNotice ?? autoTuneNotice ?? largeSolutionNotice}
              noticeAction={tunedBefore
                ? { label: t('hint.autoTunedUndo'), onClick: () => { setClassMap(tunedBefore); setTunedBefore(null) } }
                : null}
              onSelectClass={(classId) => selectItem({ type: 'class', key: classId })}
              onSelectMember={selectMember}
              onZoomChange={handleZoomChange}
              onFitZoom={fitZoom}
              onExportSvg={exportSvg}
            />
          )}
          {viewMode === 'flowmap' && flowView && (
            <FlowDiagram
              view={flowView}
              selection={selection}
              zoom={classMap.zoom}
              shellRef={shellRef}
              onSelectClass={(classId) => selectItem({ type: 'class', key: classId })}
              onSelectRow={(key, rowKind) => {
                selectItem({ type: 'method', key })
                if (rowKind === 'route') setFlowEntryId(key)
              }}
              onZoomChange={handleZoomChange}
              onExportSvg={exportSvg}
            />
          )}
          {viewMode === 'callflow' && (
            <Suspense fallback={<div className="panel-message">{t('empty.loadingGraph')}</div>}>
              <GraphView
                graphWindow={callFlowWindow}
                entryPoints={entryPoints}
                selectedNodeId={selection?.type === 'method' ? selection.key : null}
                highlightPath={highlightPath}
                onGoToSelected={goToSelectedNode}
                onSelectNode={(node) => selectItem({ type: 'method', key: node.id })}
                onDrilldown={(node) => { setHighlightPath([]); drilldown(node.id) }}
                onExpandGroup={(groupId) => setExpandedGroups((current) =>
                  current.includes(groupId) ? current : [...current, groupId])}
                onExportPng={(dataUri) => {
                  const name = (graph?.solutionPath.split(/[\\/]/).pop() ?? 'tracelens')
                    .replace(/\.(sln|csproj)$/i, '')
                  toDataUriFile(dataUri, `${name}.callflow.png`)
                  setToast(t('export.savePng'))
                }}
              />
            </Suspense>
          )}
          {viewMode === 'startup' && startupFlow && (
            <StartupFlowView
              flow={startupFlow}
              selection={selection}
              onSelect={(key) => selectItem({ type: 'startupStep', key })}
              projectKeys={projectKeys}
              projectKey={activeProjectKey}
              onChangeProject={setProjectKey}
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
                onSelect={selectItem}
                onExplain={openGlossary}
                onCopyMarkdown={copyMarkdown}
              />
            </div>
          )}
          {viewMode === 'middleware' && middlewarePipeline && (
            <div className="graph-shell">
              <MiddlewareView
                pipeline={middlewarePipeline}
                view={middlewareView}
                selection={selection}
                onSelect={selectItem}
                onExplain={openGlossary}
                onOpenSource={setSourceTarget}
                onOpenInEditor={openLocationInEditor}
                projectKeys={projectKeys}
                projectKey={activeProjectKey}
                onChangeProject={setProjectKey}
              />
            </div>
          )}
          {((viewMode === 'classmap' && !classDiagram) ||
            (viewMode === 'flowmap' && !flowView) ||
            (viewMode === 'data' && !dataAccess) ||
            (viewMode === 'middleware' && !middlewarePipeline) ||
            (viewMode === 'startup' && !startupFlow)) && (
            <div className="diagram-wrap"><div className="graph-shell">
              {viewMode === 'data'
                ? emptyBlock('empty.data', 'empty.noDataAccessFound')
                : viewMode === 'middleware'
                  ? emptyBlock('empty.middleware', 'empty.noMiddlewareFound')
                  : viewMode === 'startup'
                    ? emptyBlock('empty.startup', 'empty.noStartupFound')
                    : emptyBlock('empty.analyzeFirst', 'empty.noClassesFound')}
            </div></div>
          )}
        </section>

        <Splitter
          side="right"
          onPreview={(delta, start) => applyColumns({ left: start.left, right: start.right - delta })}
          onCommit={setColumns}
          onReset={() => setColumns(applyColumns({ ...columns, right: defaultColumns.right }))}
          columns={columns}
        />

        <div className={`right-column${collapsed.right ? ' collapsed' : ''}${viewMode === 'startup' ? ' startup-code-layout' : ''}`} id="tracelens-right-column">
          <div className="right-column-tools">
            <button
              type="button"
              className="panel-collapse"
              aria-expanded={!collapsed.right}
              title={collapsed.right ? t('view.expandPanel') : t('view.collapsePanel')}
              onClick={() => setCollapsed((current) => ({ ...current, right: !current.right }))}
            >
              {collapsed.right ? '‹' : '›'}
            </button>
          </div>
          {/*
            접기는 언마운트가 아니라 CSS로 숨긴다.
            언마운트하면 입력 중이던 질문·최근 답변·영향 결과가 사라진다(T46).
          */}
          <div className="right-column-body" hidden={collapsed.right}>
          {diffOpen && (
            <SnapshotDiffPanel
              snapshotId={graph?.snapshotId ?? ''}
              analyzing={busy}
              onReanalyze={() => analyze()}
              onClose={() => setDiffOpen(false)}
            />
          )}

          <CodeArea
            graph={graph}
            startupFlow={viewMode === 'startup' ? startupFlow : null}
            selection={selection}
            sourceTarget={sourceTarget}
            onClearSourceTarget={() => setSourceTarget(null)}
            theme={resolvedTheme}
            height={viewMode === 'startup' ? '100%' : 220}
            onSelectNode={(nodeId) => setSelection({ type: 'method', key: nodeId })}
          />

          <div className="right-tabs" role="tablist" aria-label={t('panel.selected')}>
            {(['detail', 'impact', 'ask'] as const).map((tab) => (
              <button
                key={tab}
                type="button"
                role="tab"
                id={`tracelens-right-tab-${tab}`}
                aria-selected={rightTab === tab}
                aria-controls={`tracelens-right-panel-${tab}`}
                className={rightTab === tab ? 'active' : ''}
                onClick={() => setRightTab(tab)}
              >
                {t(`right.tab.${tab}` as const)}
              </button>
            ))}
          </div>

          {/* 세 탭을 모두 마운트한 채 숨긴다. 탭을 옮겨도 각 탭의 입력·결과가 남아야 한다. */}
          <section
            className="panel detail-panel"
            id="tracelens-right-panel-detail"
            role="tabpanel"
            aria-labelledby="tracelens-right-tab-detail"
            hidden={rightTab !== 'detail'}
          >
            <header>
              <span>{t('panel.selected')}</span>
              <small>{selection ? t(`selection.${selection.type}` as const) : ''}</small>
              {selection && (
                <button
                  type="button"
                  className="link-button"
                  title={t('bookmark.add')}
                  aria-pressed={isBookmarked}
                  onClick={toggleBookmark}
                >
                  {isBookmarked ? '★' : '☆'}
                </button>
              )}
            </header>
            {isDataSelection && graph ? (
              <DataDetailPanel
                graph={graph}
                selection={selection!}
                onSelect={selectItem}
                onShowFlow={openFlow}
                onOpenSource={setSourceTarget}
                onOpenInEditor={openLocationInEditor}
              />
            ) : (
              <DetailPanel
                graph={graph}
                selection={selection}
                relations={relations}
                namespaces={namespaces}
                entryPoints={entryPoints}
                namespacePrefix={namespacePrefix}
                incomingByNode={incomingByNode}
                outgoingByNode={outgoingByNode}
                actions={detailActions}
              />
            )}
          </section>

          <div
            id="tracelens-right-panel-impact"
            role="tabpanel"
            aria-labelledby="tracelens-right-tab-impact"
            hidden={rightTab !== 'impact'}
          >
            <ImpactPanel
              nodeId={selectedNodeIdForImpact}
              nodeLabel={selectedNodeIdForImpact
                ? nodeById.get(selectedNodeIdForImpact)?.displayName ?? selectedNodeIdForImpact
                : ''}
              snapshotId={graph?.snapshotId ?? ''}
              targets={impactTargets}
              onChangeTargets={setImpactTargets}
              result={impactResult}
              onChangeResult={setImpactResult}
              onOpenEntryPoint={openEntryPointFromImpact}
              onOpenSource={setSourceTarget}
              onOpenInEditor={openLocationInEditor}
              onCopyMarkdown={copyMarkdown}
            />
          </div>

          <div
            id="tracelens-right-panel-ask"
            role="tabpanel"
            aria-labelledby="tracelens-right-tab-ask"
            hidden={rightTab !== 'ask'}
          >
            <AskPanel
              context={askContext}
              snapshotId={graph?.snapshotId ?? ''}
              question={askQuestion}
              onChangeQuestion={setAskQuestion}
              recentAnswers={recentAnswers}
              onPushRecentAnswer={pushRecentAnswer}
              onContextCopied={() => completeStep('context')}
              onExplain={openGlossary}
              onOpenSource={setSourceTarget}
              onOpenInEditor={openLocationInEditor}
            />
          </div>
          </div>
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

/**
 * 환경 점검 모달. Esc로 닫히고 포커스를 가둔다 —
 * 도움말이 "Esc가 열린 대화상자를 닫는다"고 적어 둔 이상 여기도 같아야 한다(T48).
 */
function EnvironmentDialog({
  report,
  errorId,
  onClose,
}: {
  report: DiagnosticsReport | null
  errorId: string | null
  onClose: () => void
}) {
  const { t } = useI18n()
  const modalRef = useModal<HTMLDivElement>(onClose)

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal"
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-label={t('diagnostics.title')}
        onClick={(event) => event.stopPropagation()}
      >
        <header>
          <span>{t('diagnostics.title')}</span>
          <button type="button" className="link-button" onClick={onClose}>{t('ask.close')}</button>
        </header>
        {report
          ? <DiagnosticsCard report={report} className="diagnostics-modal-body" errorId={errorId} />
          : <div className="panel-message">{t('freshness.checking')}</div>}
      </div>
    </div>
  )
}

/** 해시의 `sub`가 DB·캐시 하위 탭인지. 모르는 값은 무시한다(옛 링크·손으로 고친 URL). */
function isDataSub(value: string): value is DataViewState['sub'] {
  return ['route', 'map', 'tx', 'comp', 'check'].includes(value)
}

/** 해시의 `sub`가 미들웨어 하위 탭인지. */
function isMiddlewareSub(value: string): value is MiddlewareViewState['sub'] {
  return ['pipe', 'sc', 'check'].includes(value)
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

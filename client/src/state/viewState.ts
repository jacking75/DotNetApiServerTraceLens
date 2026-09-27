import type { WalkthroughStepId } from '../components/GuidedWalkthrough'
import type { AgentId } from '../agents'
import type { DiagramLayoutMode } from '../graph/classDiagram'
import { entrySorts, type EntrySort } from '../graph/entryGroups'
import type { RelationType } from '../graph/relations'
import type { TreeDirection } from '../graph/treeLayout'
import type { FindingSeverity, Selection, ViewMode } from '../types/graph'

export interface ClassMapState {
  layout: DiagramLayoutMode
  treeDirection: TreeDirection
  showFields: boolean
  showMethods: boolean
  relations: Record<RelationType, boolean>
  zoom: number
  focusOnly: boolean
  hiddenNamespaces: string[]
}

/** DB · 캐시 보기의 하위 보기. */
export type DataSubView = 'route' | 'map' | 'tx' | 'comp' | 'check'

export interface DataViewState {
  sub: DataSubView
  /** 실행 경로에서 고른 진입점. */
  entryNodeId: string | null
  /** 보상 처리에서 펼친 쓰기 경로. */
  pathId: string | null
  mapFilter: 'all' | 'write' | 'flagged'
  severity: 'all' | FindingSeverity
  /** 추정 판정을 접는다. */
  onlyCertain: boolean
}

/** 미들웨어 보기의 하위 보기. */
export type MiddlewareSubView = 'pipe' | 'sc' | 'check'

export interface MiddlewareViewState {
  sub: MiddlewareSubView
  /** 응답 경로(next 이후) 줄을 표시할지. */
  showPost: boolean
  severity: 'all' | FindingSeverity
}

/** 첫 사용 안내의 진행. `seen`이 false면 첫 방문이라 안내를 자동으로 연다. */
export interface GuideState {
  seen: boolean
  completed: WalkthroughStepId[]
}

export interface PersistedState {
  viewMode: ViewMode
  classMap: ClassMapState
  /** 전체 흐름도의 깊이 제한. 9는 "전체"를 뜻한다. */
  depth: number
  selection: Selection | null
  columns: { left: number; right: number }
  solutionPath: string
  dataView: DataViewState
  middlewareView: MiddlewareViewState
  /** 호출 흐름에서 고른 진입점의 엔드포인트 ID. 복원할 때 존재를 확인한 뒤에만 쓴다. */
  callFlowEndpointId: string | null
  /** 호출 흐름에서 펼쳐 들어간 노드 스택. */
  focusStack: string[]
  /** 전체 흐름도에서 고른 진입점 노드 ID. null이면 전체 지도다. */
  flowEntryId: string | null
  /** 첫 사용 안내의 진행. */
  guide: GuideState
  /** 최근에 분석한 대상(최신 우선, 최대 5개). 매번 드라이브부터 고르지 않게 한다. */
  recentTargets: string[]
  /** 테마. `system`이면 운영체제 설정(prefers-color-scheme)을 따른다. */
  theme: ThemeSetting
  /** 글꼴 배율. 모든 font-size가 이 값과 곱해진다. */
  fontScale: number
  /** 접어 둔 좌·우 패널. 접히면 세로 띠만 남고 캔버스가 그만큼 넓어진다. */
  collapsed: { left: boolean; right: boolean }
  /** 진입점 목록의 정렬. 세 보기가 같은 값을 쓴다. */
  entryListSort: EntrySort
  /** 우측 열에서 보고 있던 탭. */
  rightTab: RightTab
  /** ★로 찍어 둔 자리. 대상 경로별로 최대 50개. */
  bookmarks: Bookmark[]
  /** 통합 찾기에서 최근에 고른 항목. 빈 입력일 때 보여 준다. */
  quickFindRecent: string[]
  /** 시작 흐름·미들웨어에서 고른 실행 프로젝트. */
  projectKey: string | null
  /** 인앱 질문에 사용할 CLI. 화면 전체가 이 선택을 공유한다. */
  agent: AgentId
}

/** 우측 열의 탭. 코드 보기는 탭 위에 고정이라 여기 없다. */
export type RightTab = 'detail' | 'impact' | 'ask'

const rightTabs: readonly RightTab[] = ['detail', 'impact', 'ask']

/** ★로 찍어 둔 자리. 어제 보던 자리로 돌아가는 가장 싼 방법이다. */
export interface Bookmark {
  /** 어느 분석 대상에서 찍었는지. 대상이 바뀌면 다른 목록을 본다. */
  target: string
  selection: Selection
  label: string
  at: string
}

/** 보관하는 북마크 수. 대상 하나당 이만큼이다. */
export const maxBookmarks = 50

/** 보관하는 최근 찾기 항목 수. */
export const maxQuickFindRecent = 5

/**
 * 북마크를 더한다. 같은 선택은 위로 올리고 상한을 넘으면 오래된 것부터 버린다.
 * 대상이 다른 북마크는 건드리지 않는다(대상을 오가며 써도 서로를 밀어내지 않게).
 */
export function addBookmark(current: readonly Bookmark[], item: Bookmark): Bookmark[] {
  const sameTarget = current.filter((entry) => entry.target === item.target)
  const others = current.filter((entry) => entry.target !== item.target)
  const rest = sameTarget.filter((entry) =>
    !(entry.selection.type === item.selection.type && entry.selection.key === item.selection.key))
  return [...others, ...[item, ...rest].slice(0, maxBookmarks)]
}

/** 최근 찾기 항목을 더한다. 같은 항목은 위로 올린다. */
export function pushRecentFind(current: readonly string[], id: string): string[] {
  if (id.trim().length === 0) return [...current]
  return [id, ...current.filter((item) => item !== id)].slice(0, maxQuickFindRecent)
}

export type ThemeSetting = 'system' | 'dark' | 'light'

/** 고를 수 있는 글꼴 배율. 저장값이 이 중에 없으면 1로 돌린다. */
export const fontScales = [0.9, 1, 1.15] as const

/** 보관하는 최근 대상 수. 더 늘리면 목록이 길어져 고르기 어렵다. */
export const maxRecentTargets = 5

/**
 * 최근 대상 목록에 경로를 더한다. 같은 경로는 위로 올리고 상한을 넘으면 오래된 것부터 버린다.
 * 대소문자만 다른 같은 경로를 두 줄로 보여 주지 않는다(Windows 경로).
 */
export function addRecentTarget(current: readonly string[], path: string): string[] {
  const trimmed = path.trim()
  if (trimmed.length === 0) return [...current]
  const rest = current.filter((item) => item.toLowerCase() !== trimmed.toLowerCase())
  return [trimmed, ...rest].slice(0, maxRecentTargets)
}

export const defaultColumns = { left: 272, right: 392 }

export const defaultState: PersistedState = {
  viewMode: 'callflow',
  classMap: {
    layout: 'tree',
    treeDirection: 'LR',
    showFields: true,
    showMethods: true,
    // 의존(호출)은 기본으로 꺼 둔다. 켜면 선이 30% 늘고 대부분 다른 관계와 겹친다.
    relations: { inherit: true, implement: true, assoc: true, depend: false },
    zoom: 0.75,
    focusOnly: false,
    hiddenNamespaces: [],
  },
  depth: 3,
  selection: null,
  columns: { ...defaultColumns },
  solutionPath: '',
  dataView: {
    // 실행 경로가 첫 탭이다. 가장 어려운 "보상 처리"로 시작하면 처음 온 사람이 읽을 수 없다.
    // 이미 'comp'를 저장해 둔 사용자는 그대로 둔다(마이그레이션하지 않는다).
    sub: 'route',
    entryNodeId: null,
    pathId: null,
    mapFilter: 'all',
    severity: 'all',
    onlyCertain: false,
  },
  middlewareView: { sub: 'pipe', showPost: true, severity: 'all' },
  callFlowEndpointId: null,
  focusStack: [],
  flowEntryId: null,
  guide: { seen: false, completed: [] },
  recentTargets: [],
  theme: 'system',
  fontScale: 1,
  collapsed: { left: false, right: false },
  entryListSort: 'route',
  rightTab: 'detail',
  bookmarks: [],
  quickFindRecent: [],
  projectKey: null,
  agent: 'claude-code',
}

/** 저장값에서 읽은 안내 진행. 알 수 없는 단계 ID는 버린다(단계가 바뀌어도 깨지지 않게). */
const knownGuideSteps: readonly WalkthroughStepId[] = ['pick', 'analyze', 'entry', 'evidence', 'context']

function normalizeGuide(value: Partial<GuideState> | undefined): GuideState {
  if (!value) return { seen: false, completed: [] }
  const completed = Array.isArray(value.completed)
    ? value.completed.filter((step): step is WalkthroughStepId => knownGuideSteps.includes(step as WalkthroughStepId))
    : []
  return { seen: value.seen === true, completed }
}

const storageKey = 'tracelens.view'

/** 새로고침 후에도 보기·필터·선택을 되살린다. 저장값이 깨져 있으면 조용히 기본값으로 돌아간다. */
export function loadState(): PersistedState {
  try {
    const raw = window.localStorage.getItem(storageKey)
    if (!raw) return defaultState
    const parsed = JSON.parse(raw) as Partial<PersistedState>
    return {
      ...defaultState,
      ...parsed,
      classMap: { ...defaultState.classMap, ...parsed.classMap, relations: { ...defaultState.classMap.relations, ...parsed.classMap?.relations } },
      columns: { ...defaultColumns, ...parsed.columns },
      dataView: { ...defaultState.dataView, ...parsed.dataView },
      middlewareView: { ...defaultState.middlewareView, ...parsed.middlewareView },
      focusStack: Array.isArray(parsed.focusStack) ? parsed.focusStack : [],
      guide: normalizeGuide(parsed.guide),
      recentTargets: Array.isArray(parsed.recentTargets)
        ? parsed.recentTargets.filter((item): item is string => typeof item === 'string').slice(0, maxRecentTargets)
        : [],
      theme: parsed.theme === 'dark' || parsed.theme === 'light' ? parsed.theme : 'system',
      fontScale: fontScales.includes(parsed.fontScale as typeof fontScales[number]) ? parsed.fontScale! : 1,
      collapsed: { ...defaultState.collapsed, ...parsed.collapsed },
      entryListSort: entrySorts.includes(parsed.entryListSort as EntrySort) ? parsed.entryListSort! : 'route',
      rightTab: rightTabs.includes(parsed.rightTab as RightTab) ? parsed.rightTab! : 'detail',
      bookmarks: Array.isArray(parsed.bookmarks)
        ? parsed.bookmarks.filter((item): item is Bookmark =>
          typeof item?.target === 'string' && typeof item?.label === 'string' && item?.selection?.key !== undefined)
        : [],
      quickFindRecent: Array.isArray(parsed.quickFindRecent)
        ? parsed.quickFindRecent.filter((item): item is string => typeof item === 'string').slice(0, maxQuickFindRecent)
        : [],
      projectKey: typeof parsed.projectKey === 'string' ? parsed.projectKey : null,
      agent: parsed.agent === 'codex' ? 'codex' : 'claude-code',
    }
  } catch {
    return defaultState
  }
}

export function saveState(state: PersistedState): void {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(state))
  } catch {
    // 시크릿 모드처럼 localStorage를 못 쓰는 환경에서도 화면은 그대로 동작해야 한다.
  }
}

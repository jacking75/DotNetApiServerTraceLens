import type { DiagramLayoutMode } from '../graph/classDiagram'
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
    sub: 'comp',
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

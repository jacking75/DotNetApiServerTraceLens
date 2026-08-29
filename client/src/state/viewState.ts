import type { DiagramLayoutMode } from '../graph/classDiagram'
import type { RelationType } from '../graph/relations'
import type { TreeDirection } from '../graph/treeLayout'
import type { Selection, ViewMode } from '../types/graph'

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

export interface PersistedState {
  viewMode: ViewMode
  classMap: ClassMapState
  /** 전체 흐름도의 깊이 제한. 9는 "전체"를 뜻한다. */
  depth: number
  selection: Selection | null
  columns: { left: number; right: number }
  solutionPath: string
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

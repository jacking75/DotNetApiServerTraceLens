import { useMemo, useState } from 'react'
import {
  getCallerWindow,
  getDrilldownWindow,
  getInitialWindow,
  getPathWindow,
} from '../graph/windowing'
import type { EntryPoint, GraphDocument, GraphWindow, ViewMode } from '../types/graph'

/** 호출 흐름 창의 방향. `up`이면 고른 노드를 부르는 쪽으로 자란다. */
export type WindowDirection = 'down' | 'up'

export interface CallFlowState {
  /** 지금 보고 있는 진입점. */
  entry: EntryPoint | null
  setEntry: React.Dispatch<React.SetStateAction<EntryPoint | null>>
  /** 파고든 노드들. 마지막 항목이 현재 창의 중심이다. */
  focusStack: string[]
  setFocusStack: React.Dispatch<React.SetStateAction<string[]>>
  depth: number
  setDepth: React.Dispatch<React.SetStateAction<number>>
  direction: WindowDirection
  setDirection: React.Dispatch<React.SetStateAction<WindowDirection>>
  /** 첫 창에서 펼쳐 둔 팬아웃 요약 노드. 창이 바뀌면 비운다. */
  expandedGroups: string[]
  setExpandedGroups: React.Dispatch<React.SetStateAction<string[]>>
  /** 영향 목록에서 온 경로. 비어 있지 않으면 그 경로를 그대로 펼친다. */
  highlightPath: string[]
  setHighlightPath: React.Dispatch<React.SetStateAction<string[]>>
  /** 위 상태에서 나온 지금 그릴 창. 호출 흐름 보기가 아니면 null이다. */
  window: GraphWindow | null
}

/**
 * 호출 흐름 보기의 상태 한 벌과 거기서 나오는 창을 모은다(T72).
 *
 * App.tsx 하나가 여섯 보기의 상태를 모두 들고 있어 무엇이 어느 보기의 것인지 읽기 어려웠다.
 * 상태를 App 밖으로 옮기는 것이 아니라(상태 관리 라이브러리를 쓰지 않는다는 원칙은 그대로)
 * 한 보기에 속한 것끼리 묶어 두는 것이다.
 */
export function useCallFlowState(
  graph: GraphDocument | null,
  viewMode: ViewMode,
  initialDepth: number,
): CallFlowState {
  const [entry, setEntry] = useState<EntryPoint | null>(null)
  const [focusStack, setFocusStack] = useState<string[]>([])
  const [depth, setDepth] = useState(initialDepth)
  const [direction, setDirection] = useState<WindowDirection>('down')
  const [expandedGroups, setExpandedGroups] = useState<string[]>([])
  const [highlightPath, setHighlightPath] = useState<string[]>([])

  const window = useMemo<GraphWindow | null>(() => {
    if (!graph || viewMode !== 'callflow' || !entry) return null
    // 호출자 방향 창은 진입점이 아니라 고른 노드에서 위로 자란다.
    if (direction === 'up') {
      const target = focusStack.length > 0 ? focusStack[focusStack.length - 1] : entry.nodeId
      return getCallerWindow(graph, target)
    }

    // 영향 목록에서 온 경우에는 대상까지의 경로를 그대로 펼친다(대상이 창 밖에 있으면 이동한 보람이 없다).
    if (highlightPath.length > 0) return getPathWindow(graph, entry.nodeId, highlightPath)
    const focus = focusStack.length > 0 ? focusStack[focusStack.length - 1] : entry.nodeId
    return focusStack.length === 0
      ? getInitialWindow(graph, focus, expandedGroups)
      : getDrilldownWindow(graph, focus)
  }, [graph, viewMode, entry, focusStack, highlightPath, direction, expandedGroups])

  return {
    entry,
    setEntry,
    focusStack,
    setFocusStack,
    depth,
    setDepth,
    direction,
    setDirection,
    expandedGroups,
    setExpandedGroups,
    highlightPath,
    setHighlightPath,
    window,
  }
}

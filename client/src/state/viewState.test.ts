import { beforeEach, describe, expect, it } from 'vitest'
import { addBookmark, addRecentTarget, defaultState, loadState, maxBookmarks, maxRecentTargets, saveState } from './viewState'
import type { Bookmark } from './viewState'

/** localStorage가 없는 환경(vitest 기본)에서도 돌도록 최소 구현을 끼운다. */
function installStorage(initial: Record<string, string> = {}) {
  const store = new Map(Object.entries(initial))
  Object.defineProperty(globalThis, 'window', {
    configurable: true,
    value: {
      localStorage: {
        getItem: (key: string) => store.get(key) ?? null,
        setItem: (key: string, value: string) => void store.set(key, value),
      },
    },
  })
  return store
}

describe('viewState', () => {
  beforeEach(() => installStorage())

  it('저장한 뒤 그대로 되살린다', () => {
    const state = {
      ...defaultState,
      viewMode: 'data' as const,
      callFlowEndpointId: 'GET /orders :: Node',
      focusStack: ['A', 'B'],
      flowEntryId: 'Entry',
    }

    saveState(state)

    expect(loadState()).toEqual(state)
  })

  it('저장값이 깨져 있으면 기본값으로 돌아간다', () => {
    installStorage({ 'tracelens.view': '{이건 JSON이 아니다' })

    expect(loadState()).toEqual(defaultState)
  })

  it('focusStack이 배열이 아니면 빈 배열로 읽는다', () => {
    installStorage({ 'tracelens.view': JSON.stringify({ focusStack: 'A' }) })

    expect(loadState().focusStack).toEqual([])
  })

  it('첫 방문은 guide.seen이 false다', () => {
    expect(loadState().guide).toEqual({ seen: false, completed: [] })
  })

  it('안내 진행을 저장하고 되살린다', () => {
    saveState({ ...defaultState, guide: { seen: true, completed: ['pick', 'analyze'] } })

    expect(loadState().guide).toEqual({ seen: true, completed: ['pick', 'analyze'] })
  })

  it('저장값의 알 수 없는 안내 단계는 버린다', () => {
    installStorage({ 'tracelens.view': JSON.stringify({ guide: { seen: true, completed: ['pick', 'nope'] } }) })

    expect(loadState().guide).toEqual({ seen: true, completed: ['pick'] })
  })

  it('최근 대상은 최신이 먼저이고 중복을 지운다', () => {
    const list = addRecentTarget(addRecentTarget(['C:\\a.sln'], 'C:\\b.sln'), 'C:\\A.SLN')

    // 대소문자만 다른 같은 경로를 두 줄로 보여 주지 않는다(Windows 경로).
    expect(list).toEqual(['C:\\A.SLN', 'C:\\b.sln'])
  })

  it('최근 대상은 상한을 넘지 않는다', () => {
    let list: string[] = []
    for (let index = 0; index < maxRecentTargets + 3; index += 1) {
      list = addRecentTarget(list, `C:\\p${index}.sln`)
    }

    expect(list).toHaveLength(maxRecentTargets)
    expect(list[0]).toBe(`C:\\p${maxRecentTargets + 2}.sln`)
  })

  it('테마·글꼴 배율·패널 접힘을 저장하고 되살린다', () => {
    saveState({
      ...defaultState,
      theme: 'light',
      fontScale: 1.15,
      collapsed: { left: true, right: false },
    })

    const state = loadState()

    expect(state.theme).toBe('light')
    expect(state.fontScale).toBe(1.15)
    expect(state.collapsed).toEqual({ left: true, right: false })
  })

  /** 저장값이 손상돼도 화면이 깨지면 안 된다. 모르는 배율은 1로 돌린다. */
  it('알 수 없는 테마·배율은 기본값으로 돌린다', () => {
    installStorage({ 'tracelens.view': JSON.stringify({ theme: 'neon', fontScale: 3 }) })

    const state = loadState()

    expect(state.theme).toBe('system')
    expect(state.fontScale).toBe(1)
  })

  /**
   * 상한은 대상 하나당이다. 전체를 잘라 버리면 대상을 오가며 쓸 때
   * 다른 프로젝트의 북마크가 소리 없이 사라진다.
   */
  it('북마크 상한은 대상별로 적용되고 다른 대상을 밀어내지 않는다', () => {
    const bookmark = (target: string, key: string): Bookmark =>
      ({ target, selection: { type: 'method', key }, label: key, at: '2026-09-19T00:00:00Z' })
    let list: Bookmark[] = [bookmark('B.sln', 'keep')]

    for (let index = 0; index < maxBookmarks + 5; index += 1) {
      list = addBookmark(list, bookmark('A.sln', `node-${index}`))
    }

    expect(list.filter((item) => item.target === 'A.sln')).toHaveLength(maxBookmarks)
    expect(list.filter((item) => item.target === 'B.sln')).toHaveLength(1)
    // 같은 선택을 다시 찍으면 늘지 않고 맨 위로 올라온다.
    const again = addBookmark(list, bookmark('A.sln', 'node-10'))
    expect(again.filter((item) => item.target === 'A.sln')).toHaveLength(maxBookmarks)
    expect(again.find((item) => item.target === 'A.sln')!.selection.key).toBe('node-10')
  })

  it('없는 키는 기본값으로 채운다', () => {
    installStorage({ 'tracelens.view': JSON.stringify({ viewMode: 'classmap' }) })

    const state = loadState()

    expect(state.viewMode).toBe('classmap')
    expect(state.dataView).toEqual(defaultState.dataView)
    expect(state.callFlowEndpointId).toBeNull()
  })
})

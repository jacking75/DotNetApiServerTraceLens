import type { Selection, ViewMode } from '../types/graph'

/**
 * URL 해시로 재현할 수 있는 화면 위치.
 * 루프백 URL이라 외부에 공개되지 않고(설계 제약 4), 팀 공유는 기존 공유 보고서가 맡는다.
 * 여기서 담는 것은 "어느 화면을 보고 있었나"뿐이고 소스나 분석 결과는 담지 않는다.
 */
export interface UrlState {
  view: ViewMode
  /** 호출 흐름에서 고른 진입점의 엔드포인트 ID. */
  entry: string | null
  /** 호출 흐름에서 파고든 노드 스택. */
  focus: string[]
  selection: Selection | null
}

/** 선택 종류를 해시에 쓰는 짧은 이름으로 줄인다. 되돌릴 수 있어야 하므로 표를 양방향으로 쓴다. */
const selectionTypes: Record<string, Selection['type']> = {
  method: 'method',
  class: 'class',
  ns: 'namespace',
  startup: 'startupStep',
  finding: 'finding',
  path: 'writePath',
  source: 'dataSource',
  target: 'storageTarget',
  rule: 'rule',
  mw: 'middlewareStep',
}

const selectionCodes = Object.fromEntries(
  Object.entries(selectionTypes).map(([code, type]) => [type, code]),
) as Record<Selection['type'], string>

/**
 * 화면 위치를 해시 문자열로 만든다.
 * 값이 없는 항목은 아예 넣지 않는다 — `#view=callflow&entry=&focus=`처럼 빈 값이 늘어서면 읽기 어렵다.
 */
export function serialize(state: UrlState): string {
  const parts = [`view=${state.view}`]
  if (state.entry) parts.push(`entry=${encodeURIComponent(state.entry)}`)
  if (state.focus.length > 0) parts.push(`focus=${state.focus.map(encodeURIComponent).join(',')}`)
  if (state.selection) {
    parts.push(`sel=${selectionCodes[state.selection.type]}:${encodeURIComponent(state.selection.key)}`)
  }

  return `#${parts.join('&')}`
}

const viewModes: ViewMode[] = ['callflow', 'classmap', 'flowmap', 'startup', 'data', 'middleware']

/**
 * 해시를 화면 위치로 되돌린다.
 * 모르는 키와 모르는 값은 무시한다 — 옛 링크나 손으로 고친 URL 때문에 화면이 깨지면 안 된다.
 * 되돌린 ID가 지금 그래프에 있는지는 호출자가 확인한다(여기서는 그래프를 모른다).
 */
export function parse(hash: string): Partial<UrlState> {
  const text = hash.startsWith('#') ? hash.slice(1) : hash
  if (text.length === 0) return {}

  const result: Partial<UrlState> = {}
  for (const part of text.split('&')) {
    const separator = part.indexOf('=')
    if (separator <= 0) continue
    const key = part.slice(0, separator)
    const value = part.slice(separator + 1)

    if (key === 'view') {
      const view = value as ViewMode
      if (viewModes.includes(view)) result.view = view
      continue
    }

    if (key === 'entry') {
      result.entry = decodeURIComponent(value)
      continue
    }

    if (key === 'focus') {
      result.focus = value.split(',').filter(Boolean).map(decodeURIComponent)
      continue
    }

    if (key === 'sel') {
      const colon = value.indexOf(':')
      if (colon <= 0) continue
      const type = selectionTypes[value.slice(0, colon)]
      if (!type) continue
      result.selection = { type, key: decodeURIComponent(value.slice(colon + 1)) } as Selection
    }
  }

  return result
}

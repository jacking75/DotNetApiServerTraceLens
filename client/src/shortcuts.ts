import type { TranslationKey } from './i18n/ko'

/**
 * 실제로 동작하는 단축키의 **유일한 정의**.
 * 바인딩과 도움말 표가 같은 목록을 읽어야 어긋날 수 없다 —
 * 예전에는 도움말이 "Ctrl+휠 다이어그램 확대"라고 적었지만 호출 흐름은 맨 휠로 줌했다.
 */
export interface Shortcut {
  id: string
  /** 표시용 키 조합. 번역하지 않는다(키 이름은 어느 언어에서나 같다). */
  keys: string
  /** 무엇을 하는지. */
  bodyKey: TranslationKey
  /** 어디에 포커스가 있어야 도는지. */
  scopeKey: TranslationKey
  /** 실제 바인딩이 이 정의를 쓴다. 없으면 브라우저·컴포넌트가 직접 처리하는 것이다. */
  matches?: (event: KeyboardEvent) => boolean
}

const isTypingTarget = (target: EventTarget | null) =>
  target instanceof HTMLInputElement ||
  target instanceof HTMLTextAreaElement ||
  (target as HTMLElement | null)?.isContentEditable === true

export const shortcuts: readonly Shortcut[] = [
  {
    id: 'quickFind',
    keys: 'Ctrl/Cmd + K',
    bodyKey: 'help.key.quickFindBody',
    scopeKey: 'help.scope.global',
    // 검색어를 치다 팔레트가 뜨면 안 된다. 입력·편집 중에는 가로채지 않는다.
    matches: (event) => (event.ctrlKey || event.metaKey) &&
      event.key.toLowerCase() === 'k' &&
      !isTypingTarget(event.target),
  },
  {
    id: 'escape',
    keys: 'Esc',
    bodyKey: 'help.key.escapeBody',
    scopeKey: 'help.scope.dialog',
  },
  {
    id: 'zoom',
    keys: 'Ctrl + Wheel',
    bodyKey: 'help.key.zoomBody',
    scopeKey: 'help.scope.canvas',
  },
  {
    id: 'scroll',
    keys: 'Wheel',
    bodyKey: 'help.key.scrollBody',
    scopeKey: 'help.scope.canvas',
  },
  {
    id: 'zoomStep',
    keys: 'Ctrl + + / − / 0',
    bodyKey: 'help.key.zoomStepBody',
    scopeKey: 'help.scope.canvas',
  },
  {
    id: 'splitter',
    keys: '← / → · Shift + ← / → · Home',
    bodyKey: 'help.key.splitterBody',
    scopeKey: 'help.scope.splitter',
  },
  {
    id: 'listMove',
    keys: '↑ / ↓ / Home / End',
    bodyKey: 'help.key.listMoveBody',
    scopeKey: 'help.scope.list',
  },
  {
    id: 'selectOnly',
    keys: 'Shift + Click',
    bodyKey: 'help.key.selectOnlyBody',
    scopeKey: 'help.scope.callflow',
  },
  {
    id: 'openEditor',
    keys: 'Shift + Click · Right Click',
    bodyKey: 'help.key.openEditorBody',
    scopeKey: 'help.scope.sourceLink',
  },
]

/** 바인딩이 있는 단축키를 id로 찾는다. */
export function shortcutById(id: string): Shortcut | undefined {
  return shortcuts.find((item) => item.id === id)
}

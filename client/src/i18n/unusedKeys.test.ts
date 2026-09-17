import { describe, expect, it } from 'vitest'
import { ko } from './ko'

/**
 * 템플릿으로 만들어지는 키(`t(`view.${mode}`)`)는 문자열로 찾을 수 없다.
 * 접두사를 여기 적어 두면 "이 접두사 아래는 동적으로 쓴다"가 명시적으로 남는다.
 * 새 접두사를 더할 때는 실제로 그 자리를 만드는 코드가 있어야 한다.
 */
const dynamicPrefixes = [
  'view.', // t(`view.${viewMode}`)
  'viewHint.', // ViewToolbar가 보기 이름으로 만든다
  'toolbar.data.', // DataControls가 sub 이름으로 만든다
  'toolbar.mw.', // MiddlewareControls가 sub 이름으로 만든다
  'relation.', // ClassMapControls가 관계 종류로 만든다
  'diag.', // WelcomePanel이 진단 code로 만든다
  'diagAction.', // 같은 자리에서 조치 문장을 만든다
  'error.', // translateError가 서버 오류 code로 만든다
  'help.tab.', // HelpDialog가 탭 id로 만든다
  'glossary.', // HelpDialog·HelpLink가 용어 id로 만든다
]

/** 사전 두 개를 뺀 클라이언트 소스 전체. Vite가 빌드 시각에 읽어 넣는다. */
const sources = import.meta.glob('../**/*.{ts,tsx}', { eager: true, query: '?raw', import: 'default' })

describe('i18n 미사용 키', () => {
  /**
   * 쓰지 않는 문구가 사전에 남아 있으면 "이 기능은 만들다 말았다"가 조용히 쌓인다.
   * 실제 결함: T20~T25가 쓰려던 키 10여 개가 참조 0회로 남아 있었다.
   */
  it('ko.ts의 모든 키가 client/src에서 한 번 이상 참조된다', () => {
    const blob = Object.entries(sources)
      .filter(([path]) => !path.endsWith('/ko.ts') && !path.endsWith('/en.ts'))
      .map(([, text]) => text as string)
      .join('\n')
    const unused = Object.keys(ko)
      .filter((key) => !dynamicPrefixes.some((prefix) => key.startsWith(prefix)))
      .filter((key) => !blob.includes(`'${key}'`) && !blob.includes(`\`${key}\``))
    expect(unused).toEqual([])
  })
})

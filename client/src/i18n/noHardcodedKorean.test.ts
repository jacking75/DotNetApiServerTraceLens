import { describe, expect, it } from 'vitest'
import { en } from './en'
import { ko } from './ko'

/**
 * 사용자에게 보이는 문자열은 `i18n/ko.ts`에 키로 넣어야 한다(영어는 그 키로 타입이 제한된다).
 * 주석은 한국어로 쓰므로 검사에서 뺀다 — 주석까지 영어로 옮기라는 규칙이 아니다.
 *
 * 허용 목록은 "아직 옮기지 못한 곳"이 아니라 "옮기면 안 되는 곳"만 담는다.
 * 옮기지 않은 것이 남아 있으면 이 테스트가 실패해서 눈에 띈다.
 */
const allowed: ReadonlyArray<{ file: string; reason: string }> = [
  {
    file: '../content/recipes.ts',
    reason: '레시피는 한국어·영어 문장을 짝으로 들고 있어서 사전으로 옮기지 않는다(titleKo/titleEn).',
  },
]

/**
 * Vite가 빌드 시점에 소스를 문자열로 넣어 준다. Node fs를 쓰지 않아 브라우저 환경에서도 돈다.
 *
 * 범위는 `src/**`다. 예전에는 `components`·`content`·`App.tsx`만 봐서
 * `graph/`·`api/`의 한국어가 그대로 새어 나갔다(T72).
 */
const sources = import.meta.glob('../**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

const appSource = import.meta.glob('../App.tsx', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

/**
 * 사전 자체와 테스트·벤치는 검사 대상이 아니다(한국어가 있는 것이 정상이다).
 * 이 파일과 같은 폴더(`i18n/`)는 glob이 `./`로 돌려준다.
 */
const dictionaries = ['./ko.ts', './en.ts']

function isChecked(path: string): boolean {
  if (/\.(test|bench)\.tsx?$/.test(path)) return false
  return !dictionaries.includes(path)
}

/** 문자열·템플릿 리터럴 안의 `//`를 주석으로 오해하지 않도록 직접 훑는다. */
export function stripComments(source: string): string {
  let result = ''
  let index = 0
  let quote: string | null = null
  while (index < source.length) {
    const char = source[index]
    const next = source[index + 1]
    if (quote) {
      if (char === '\\') {
        result += '  '
        index += 2
        continue
      }

      if (char === quote) quote = null
      result += char
      index += 1
      continue
    }

    if (char === '"' || char === "'" || char === '`') {
      quote = char
      result += char
      index += 1
      continue
    }

    if (char === '/' && next === '/') {
      while (index < source.length && source[index] !== '\n') index += 1
      continue
    }

    if (char === '/' && next === '*') {
      index += 2
      while (index < source.length && !(source[index] === '*' && source[index + 1] === '/')) index += 1
      index += 2
      continue
    }

    result += char
    index += 1
  }

  return result
}

describe('사용자에게 보이는 문자열', () => {
  it('컴포넌트에 하드코딩된 한국어가 남아 있지 않다', () => {
    const files = { ...sources, ...appSource }
    const offenders: string[] = []
    for (const [path, source] of Object.entries(files)) {
      if (!isChecked(path)) continue
      if (allowed.some((item) => item.file === path)) continue
      stripComments(source).split('\n').forEach((line, index) => {
        if (/[가-힣]/.test(line)) offenders.push(`${path}:${index + 1} ${line.trim()}`)
      })
    }

    expect(offenders).toEqual([])
  })

  it('검사 대상 파일을 실제로 읽었다', () => {
    // glob 경로가 어긋나면 위 검사가 조용히 통과한다. 파일 수와 범위를 확인한다.
    const checked = Object.keys(sources).filter(isChecked)
    expect(checked.length).toBeGreaterThan(30)
    expect(Object.keys(appSource).length).toBe(1)
    // `components` 밖도 실제로 들어와 있어야 한다.
    expect(checked.some((path) => path.startsWith('../graph/'))).toBe(true)
    expect(checked.some((path) => path.startsWith('../api/'))).toBe(true)
    expect(checked.some((path) => path.startsWith('../state/'))).toBe(true)
  })

  it('ko.ts의 키와 en.ts의 키가 같다', () => {
    expect(Object.keys(en).sort()).toEqual(Object.keys(ko).sort())
  })

  it('en.ts에 한국어가 남아 있지 않다', () => {
    // 언어 전환 버튼은 각 언어 이름을 그 언어로 쓴다. lang.* 는 일부러 번역하지 않는다.
    const offenders = Object.entries(en)
      .filter(([key]) => !key.startsWith('lang.'))
      .filter(([, value]) => /[가-힣]/.test(value))
    expect(offenders).toEqual([])
  })
})

import { describe, expect, it } from 'vitest'
import { ruleText, translatedRuleIds } from './ruleText'
import type { RuleInfo } from '../types/graph'

/**
 * 규칙 문구의 영어는 분석기가 아니라 여기(클라이언트)에 있다. 분석기에 로케일을 넣으면 같은 코드가
 * 언어에 따라 다른 graph.json이 되기 때문이다. 그래서 분석기가 규칙을 늘리면 이 표가 조용히 뒤처진다.
 * 이 테스트는 분석기 소스에서 규칙 ID를 직접 읽어 그 뒤처짐을 잡는다.
 */
function analyzerRuleIds(): string[] {
  // Node 타입에 기대지 않으려고 fs 대신 Vite의 glob으로 읽는다(`noHardcodedKorean.test.ts`와 같은 방식).
  const sources = import.meta.glob(
    '../../../src/TraceLens.Analyzer/{DataRuleEngine,MiddlewareScanner}.cs',
    { query: '?raw', import: 'default', eager: true },
  ) as Record<string, string>
  const ids = new Set<string>()
  for (const source of Object.values(sources)) {
    // 카탈로그 항목은 항상 `new("XXX-001", "그룹", ...)` 형태로 시작한다.
    for (const match of source.matchAll(/new\("([A-Z]+-\d+)"/g)) ids.add(match[1])
  }

  return [...ids].sort()
}

const rule = (id: string): RuleInfo => ({
  id,
  group: '그룹',
  name: '한국어 이름',
  severity: 'Warning',
  judge: 'Certain',
  description: '한국어 설명',
  shardOnly: false,
  enabled: true,
})

describe('규칙 문구', () => {
  it('분석기 카탈로그의 모든 규칙에 영어 문구가 있다', () => {
    expect([...translatedRuleIds].sort()).toEqual(analyzerRuleIds())
  })

  it('한국어에서는 분석기가 준 문구를 그대로 쓴다', () => {
    const text = ruleText(rule('TX-001'), 'ko')

    expect(text.name).toBe('한국어 이름')
    expect(text.description).toBe('한국어 설명')
  })

  it('영어에서는 표의 문구로 바꿔 끼운다', () => {
    const text = ruleText(rule('TX-001'), 'en')

    expect(text.name).toBe('External call inside a transaction')
    expect(text.group).toBe('Transaction')
  })

  it('표에 없는 규칙은 빈칸이 아니라 분석기 문구를 그대로 둔다', () => {
    const text = ruleText(rule('NEW-999'), 'en')

    expect(text.name).toBe('한국어 이름')
  })

  it('영어 문구에 한국어가 섞이지 않는다', () => {
    for (const id of translatedRuleIds) {
      const text = ruleText(rule(id), 'en')
      expect(`${text.group} ${text.name} ${text.description}`).not.toMatch(/[가-힣]/)
    }
  })
})

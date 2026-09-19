import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

/**
 * Vite의 `?raw`는 vitest에서 CSS를 빈 문자열로 준다. 이 테스트는 Node 환경이라 파일을 직접 읽는다.
 */
const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')

/**
 * 라이트 테마가 깨지는 방식은 늘 같다 — 어두운 색을 선택자에 직접 박아 두고
 * 테마 블록에서 그중 일부만 재정의한다. 그러면 재정의하지 않은 자리에서
 * "어두운 배경 위 어두운 글자"가 된다(3차 점검의 E2).
 *
 * 그래서 규칙을 하나로 정한다: **색은 `:root`와 `[data-theme]` 블록 안에서만 값으로 쓴다.**
 * 그 밖에서는 `var(--...)`만 쓴다. 이 테스트가 그 규칙을 지킨다.
 *
 * 예외는 여기 목록에 사유와 함께 적는다. 지금은 없다.
 */
const allowed: ReadonlyArray<{ selector: string; reason: string }> = []

/** 색을 담을 수 있는 속성. `box-shadow`는 rgba()를 쓰는 자리가 있어 검사에서 뺀다. */
const colorProperties = /(background(?:-color)?|color|border(?:-color)?|fill|stroke|outline-color)\s*:\s*([^;}]+)/g
const hexColor = /#[0-9a-fA-F]{3,8}/

/** `:root {`·`[data-theme="light"] {` 블록 안인지. 값이 정의되는 자리라 검사하지 않는다. */
function themeBlockLines(source: string): Set<number> {
  const inside = new Set<number>()
  let open = false
  source.split('\n').forEach((line, index) => {
    const trimmed = line.trim()
    if (trimmed.startsWith(':root') || trimmed.startsWith('[data-theme="light"] {')) open = true
    if (open) {
      inside.add(index)
      if (trimmed.endsWith('}')) open = false
    }
  })

  return inside
}

describe('테마', () => {
  it('styles.css를 실제로 읽었다', () => {
    expect(css.length).toBeGreaterThan(1000)
  })

  it('테마 블록 밖에 하드코딩한 색이 없다', () => {
    const themeLines = themeBlockLines(css)
    const offenders: string[] = []
    css.split('\n').forEach((line, index) => {
      if (themeLines.has(index)) return
      if (allowed.some((item) => line.includes(item.selector))) return
      for (const match of line.matchAll(colorProperties)) {
        if (hexColor.test(match[2])) offenders.push(`${index + 1}: ${line.trim()}`)
      }
    })

    expect(offenders).toEqual([])
  })

  /** 두 테마가 같은 변수 집합을 정의해야 한 쪽에서만 색이 빠지지 않는다. */
  it('두 테마가 같은 변수를 정의한다', () => {
    const block = (start: string) => {
      const from = css.indexOf(start)
      const to = css.indexOf('\n}', from)
      return new Set([...css.slice(from, to).matchAll(/(--[a-z0-9-]+)\s*:/g)].map((match) => match[1]))
    }

    const dark = block(':root {')
    const light = block('[data-theme="light"] {')
    // 글꼴 배율은 테마와 무관해서 라이트 블록에 없다.
    dark.delete('--fs-scale')
    expect([...dark].filter((name) => !light.has(name))).toEqual([])
    expect([...light].filter((name) => !dark.has(name))).toEqual([])
  })

  /**
   * 글자색은 두 테마 모두에서 같은 이름의 표면 위에 그려진다.
   * 라이트 테마의 `--ink-*`가 밝으면 흰 배경에서 읽을 수 없다 — 그 경우를 여기서 막는다.
   */
  it('라이트 테마의 글자색이 밝은 배경 위에서 읽힌다', () => {
    const from = css.indexOf('[data-theme="light"] {')
    const to = css.indexOf('\n}', from)
    const light = css.slice(from, to)
    const tooLight: string[] = []
    for (const match of light.matchAll(/(--ink[a-z0-9-]*)\s*:\s*#([0-9a-fA-F]{6})/g)) {
      const [, name, value] = match
      // 버튼 위의 흰 글자처럼 일부러 밝은 것은 `--ink-15`(순백)뿐이다.
      if (name === '--ink-15') continue
      const rgb = [0, 2, 4].map((offset) => parseInt(value.slice(offset, offset + 2), 16))
      if (contrast(rgb, [246, 248, 252]) < 4.5) tooLight.push(`${name}: #${value}`)
    }

    expect(tooLight).toEqual([])
  })
})

function contrast(first: number[], second: number[]): number {
  const luminance = (rgb: number[]) => {
    const channel = (value: number) => {
      const normalized = value / 255
      return normalized <= 0.04045 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4
    }

    return 0.2126 * channel(rgb[0]) + 0.7152 * channel(rgb[1]) + 0.0722 * channel(rgb[2])
  }

  const a = luminance(first)
  const b = luminance(second)
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

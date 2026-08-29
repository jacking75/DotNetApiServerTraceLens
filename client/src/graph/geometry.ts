export interface Rect {
  x: number
  y: number
  w: number
  h: number
}

export interface Point {
  x: number
  y: number
}

/** 두 박스 사이에서 서로 마주 보는 변의 접점을 고른다. */
export function sideAnchors(a: Rect, b: Rect): [Point, Point, 'h' | 'v'] {
  const ac = { x: a.x + a.w / 2, y: a.y + a.h / 2 }
  const bc = { x: b.x + b.w / 2, y: b.y + b.h / 2 }
  const dx = bc.x - ac.x
  const dy = bc.y - ac.y
  if (Math.abs(dx) >= Math.abs(dy)) {
    return dx >= 0
      ? [{ x: a.x + a.w, y: ac.y }, { x: b.x, y: bc.y }, 'h']
      : [{ x: a.x, y: ac.y }, { x: b.x + b.w, y: bc.y }, 'h']
  }

  return dy >= 0
    ? [{ x: ac.x, y: a.y + a.h }, { x: bc.x, y: b.y }, 'v']
    : [{ x: ac.x, y: a.y }, { x: bc.x, y: b.y + b.h }, 'v']
}

/** 마주 보는 옆면을 잇는 곡선. 층을 거슬러 되돌아가는 선(순환)과 패키지 배치가 쓴다. */
export function bezierBetween(a: Rect, b: Rect): string {
  const [p1, p2, axis] = sideAnchors(a, b)
  const off = 40
  if (axis === 'h') {
    const k = p2.x > p1.x ? off : -off
    return `M ${p1.x} ${p1.y} C ${p1.x + k} ${p1.y}, ${p2.x - k} ${p2.y}, ${p2.x} ${p2.y}`
  }

  const k = p2.y > p1.y ? off : -off
  return `M ${p1.x} ${p1.y} C ${p1.x} ${p1.y + k}, ${p2.x} ${p2.y - k}, ${p2.x} ${p2.y}`
}

/**
 * 점 목록을 진행축 방향으로 부드럽게 잇는다. 제어점을 진행축으로만 뻗어서
 * 층 사이 흐름(가로 또는 세로)이 눈에 유지된다.
 */
export function smoothPath(points: Point[], horizontal: boolean): string {
  let d = `M ${points[0].x} ${points[0].y}`
  for (let index = 1; index < points.length; index += 1) {
    const previous = points[index - 1]
    const current = points[index]
    if (horizontal) {
      const k = Math.max(16, Math.abs(current.x - previous.x) / 2) * (current.x > previous.x ? 1 : -1)
      d += ` C ${previous.x + k} ${previous.y}, ${current.x - k} ${current.y}, ${current.x} ${current.y}`
    } else {
      const k = Math.max(16, Math.abs(current.y - previous.y) / 2) * (current.y > previous.y ? 1 : -1)
      d += ` C ${previous.x} ${previous.y + k}, ${current.x} ${current.y - k}, ${current.x} ${current.y}`
    }
  }

  return d
}

/**
 * 캔버스(DOM + SVG)를 그대로 SVG 한 장으로 굳힌다.
 * 회의·PR에 붙일 그림이 필요할 때 화면을 다시 만들지 않아도 되게 하는 것이 목적이다.
 * 계산 과정이 없는 순수 변환이라 React에 의존하지 않는다.
 */

/** SVG에 인라인으로 넣을 스타일 속성. 외부 CSS 없이 열려야 하므로 값을 직접 박는다. */
const inlinedProperties = [
  'color', 'background-color', 'background-image', 'border', 'border-radius', 'font-family', 'font-size',
  'font-weight', 'line-height', 'padding', 'margin', 'opacity', 'text-align', 'white-space', 'overflow',
  'box-sizing', 'display', 'width', 'height',
] as const

function inlineStyles(source: Element, clone: Element): void {
  const computed = window.getComputedStyle(source)
  const declarations = inlinedProperties
    .map((property) => `${property}:${computed.getPropertyValue(property)}`)
    .filter((entry) => !entry.endsWith(':'))
  clone.setAttribute('style', declarations.join(';'))

  const sourceChildren = [...source.children]
  const cloneChildren = [...clone.children]
  for (let index = 0; index < sourceChildren.length; index += 1) {
    const child = cloneChildren[index]
    if (child) inlineStyles(sourceChildren[index], child)
  }
}

export interface SvgExportOptions {
  width: number
  height: number
  /** 배경색. 투명하게 두면 어두운 뷰어에서 글자가 안 보인다. */
  background: string
}

/**
 * 절대 위치 DOM 박스와 그 아래 SVG 선을 합쳐 한 장의 SVG 문자열로 만든다.
 * DOM 부분은 `foreignObject`에 그대로 넣고, 스타일은 인라인으로 굳힌다.
 */
export function buildSvg(canvas: HTMLElement, options: SvgExportOptions): string {
  const clone = canvas.cloneNode(true) as HTMLElement
  inlineStyles(canvas, clone)
  // 줌 transform은 내보낸 그림에서 의미가 없다. 원래 크기로 되돌린다.
  clone.style.transform = 'none'

  const serialized = new XMLSerializer().serializeToString(clone)
  return [
    `<svg xmlns="http://www.w3.org/2000/svg" width="${Math.ceil(options.width)}"`,
    ` height="${Math.ceil(options.height)}" viewBox="0 0 ${Math.ceil(options.width)} ${Math.ceil(options.height)}">`,
    `<rect width="100%" height="100%" fill="${options.background}"/>`,
    `<foreignObject width="100%" height="100%">`,
    `<div xmlns="http://www.w3.org/1999/xhtml">${serialized}</div>`,
    `</foreignObject></svg>`,
  ].join('')
}

/** 만든 SVG를 파일로 내려받는다. 저장 위치는 브라우저가 묻는다. */
export function toSvgFile(svg: string, fileName: string): void {
  const blob = new Blob([svg], { type: 'image/svg+xml;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

/** 데이터 URI(Cytoscape의 `png()` 결과 등)를 파일로 내려받는다. */
export function toDataUriFile(dataUri: string, fileName: string): void {
  const link = document.createElement('a')
  link.href = dataUri
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
}

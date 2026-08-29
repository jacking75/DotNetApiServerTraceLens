import { useRef, type PointerEvent as ReactPointerEvent } from 'react'

interface SplitterProps {
  side: 'left' | 'right'
  columns: { left: number; right: number }
  onDrag: (delta: number, start: { left: number; right: number }) => void
  onReset: () => void
}

/**
 * 3분할 패널 사이의 경계선. 드래그 중에는 CSS 변수만 바꾸고 state는 드래그가 끝날 때만 반영해야
 * 리렌더가 안 생겨서 부드럽게 움직인다(폭 계산과 클램프는 App의 applyColumns가 한다).
 */
export function Splitter({ side, columns, onDrag, onReset }: SplitterProps) {
  const startRef = useRef({ x: 0, left: 0, right: 0 })
  const elementRef = useRef<HTMLDivElement>(null)

  const handlePointerDown = (event: ReactPointerEvent<HTMLDivElement>) => {
    event.preventDefault()
    startRef.current = { x: event.clientX, left: columns.left, right: columns.right }
    elementRef.current?.setPointerCapture(event.pointerId)
    elementRef.current?.classList.add('dragging')
    document.body.classList.add('resizing')
  }

  const handlePointerMove = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (!elementRef.current?.hasPointerCapture(event.pointerId)) return
    onDrag(event.clientX - startRef.current.x, startRef.current)
  }

  const handlePointerUp = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (!elementRef.current?.hasPointerCapture(event.pointerId)) return
    elementRef.current.releasePointerCapture(event.pointerId)
    elementRef.current.classList.remove('dragging')
    document.body.classList.remove('resizing')
  }

  return (
    <div
      ref={elementRef}
      className="splitter"
      role="separator"
      aria-orientation="vertical"
      title={`드래그해서 ${side === 'left' ? '좌측' : '우측'} 패널 폭 조절 · 더블클릭하면 기본값`}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onPointerCancel={handlePointerUp}
      onDoubleClick={onReset}
    />
  )
}

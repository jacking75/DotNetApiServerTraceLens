import { useRef, type KeyboardEvent as ReactKeyboardEvent, type PointerEvent as ReactPointerEvent } from 'react'
import { useI18n } from '../i18n/I18nProvider'

interface SplitterProps {
  side: 'left' | 'right'
  columns: { left: number; right: number }
  /** 드래그 중. CSS 변수만 바꾸고 state는 건드리지 않는다. 클램프한 폭을 돌려준다. */
  onPreview: (delta: number, start: { left: number; right: number }) => { left: number; right: number }
  /** 드래그가 끝난 뒤 한 번. 여기서만 state를 바꾼다. */
  onCommit: (columns: { left: number; right: number }) => void
  onReset: () => void
}

/**
 * 3분할 패널 사이의 경계선.
 * 드래그 중에는 CSS 변수만 바꾸고 state는 드래그가 끝날 때 한 번만 반영한다 —
 * pointermove마다 setState를 부르면 저장 effect·배치 계산·클래스 다이어그램이 연쇄로 돈다.
 */
export function Splitter({ side, columns, onPreview, onCommit, onReset }: SplitterProps) {
  const { t } = useI18n()
  const startRef = useRef({ x: 0, left: 0, right: 0 })
  const latestRef = useRef(columns)
  const elementRef = useRef<HTMLDivElement>(null)

  const handlePointerDown = (event: ReactPointerEvent<HTMLDivElement>) => {
    event.preventDefault()
    startRef.current = { x: event.clientX, left: columns.left, right: columns.right }
    latestRef.current = columns
    elementRef.current?.setPointerCapture(event.pointerId)
    elementRef.current?.classList.add('dragging')
    document.body.classList.add('resizing')
  }

  const handlePointerMove = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (!elementRef.current?.hasPointerCapture(event.pointerId)) return
    latestRef.current = onPreview(event.clientX - startRef.current.x, startRef.current)
  }

  const handlePointerUp = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (!elementRef.current?.hasPointerCapture(event.pointerId)) return
    elementRef.current.releasePointerCapture(event.pointerId)
    elementRef.current.classList.remove('dragging')
    document.body.classList.remove('resizing')
    onCommit(latestRef.current)
  }

  /** 마우스 없이도 폭을 바꿀 수 있어야 한다. 화살표 키 한 번에 16px, Home은 기본값으로 되돌린다. */
  const handleKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    const step = event.shiftKey ? 64 : 16
    if (event.key === 'ArrowLeft') {
      event.preventDefault()
      onCommit(onPreview(-step, columns))
      return
    }

    if (event.key === 'ArrowRight') {
      event.preventDefault()
      onCommit(onPreview(step, columns))
      return
    }

    if (event.key === 'Home') {
      event.preventDefault()
      onReset()
    }
  }

  return (
    <div
      ref={elementRef}
      className="splitter"
      role="separator"
      tabIndex={0}
      aria-orientation="vertical"
      aria-valuenow={Math.round(side === 'left' ? columns.left : columns.right)}
      aria-label={t('split.hint', { side: side === 'left' ? t('split.left') : t('split.right') })}
      title={t('split.hint', { side: side === 'left' ? t('split.left') : t('split.right') })}
      onKeyDown={handleKeyDown}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onPointerCancel={handlePointerUp}
      onDoubleClick={onReset}
    />
  )
}

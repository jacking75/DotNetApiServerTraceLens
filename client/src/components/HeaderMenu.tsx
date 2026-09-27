import { useEffect, useRef, useState, type ReactNode } from 'react'

interface HeaderMenuProps {
  label: string
  title?: string
  children: ReactNode
  /** 메뉴 안에서 무엇을 고르면 닫는다. 항목마다 닫기를 부르지 않게 한 곳에서 처리한다. */
  id: string
}

/**
 * 헤더의 드롭다운 하나. 컨트롤 15개를 한 줄에 늘어놓으면 1600px에서 마지막 버튼이 잘린다.
 * 키보드로 열고(Enter/Space) 닫을 수 있어야 하므로 `details`가 아니라 버튼 + 패널로 만든다.
 */
export function HeaderMenu({ label, title, children, id }: HeaderMenuProps) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const focusEdge = useRef<'first' | 'last' | null>(null)

  const items = () => [...(ref.current?.querySelectorAll<HTMLElement>(
    '.header-menu-panel button:not([disabled]), .header-menu-panel a[href], .header-menu-panel select:not([disabled])',
  ) ?? [])]

  useEffect(() => {
    if (!open || !focusEdge.current) return
    const options = items()
    ;(focusEdge.current === 'last' ? options.at(-1) : options[0])?.focus()
    focusEdge.current = null
  }, [open])

  useEffect(() => {
    if (!open) return
    const onPointerDown = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) setOpen(false)
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && ref.current?.contains(event.target as Node)) {
        event.preventDefault()
        setOpen(false)
        triggerRef.current?.focus()
      }
    }

    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  return (
    <div className="header-menu" ref={ref} onBlur={(event) => {
      if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false)
    }}>
      <button
        ref={triggerRef}
        type="button"
        className="browse"
        aria-expanded={open}
        aria-controls={id}
        title={title ?? label}
        onClick={() => setOpen((value) => !value)}
        onKeyDown={(event) => {
          if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return
          event.preventDefault()
          const edge = event.key === 'ArrowUp' ? 'last' : 'first'
          if (open) {
            const options = items()
            ;(edge === 'last' ? options.at(-1) : options[0])?.focus()
          } else {
            focusEdge.current = edge
            setOpen(true)
          }
        }}
      >
        {label} <span aria-hidden="true">▾</span>
      </button>
      {open && (
        <div className="header-menu-panel" id={id} role="group" aria-label={label}
          onClick={(event) => {
            if (!(event.target instanceof Element) || !event.target.closest('button:not([disabled]), a[href]')) return
            triggerRef.current?.focus()
            setOpen(false)
          }}
          onKeyDown={(event) => {
            if (event.target instanceof HTMLSelectElement) return
            if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return
            const options = items()
            if (options.length === 0) return
            event.preventDefault()
            const index = options.indexOf(event.target as HTMLElement)
            const next = event.key === 'Home' ? 0 : event.key === 'End' ? options.length - 1
              : (index + (event.key === 'ArrowDown' ? 1 : -1) + options.length) % options.length
            options[next]?.focus()
          }}>
          {children}
        </div>
      )}
    </div>
  )
}

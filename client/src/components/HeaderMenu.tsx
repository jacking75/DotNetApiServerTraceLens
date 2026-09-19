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

  useEffect(() => {
    if (!open) return
    const onPointerDown = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) setOpen(false)
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false)
    }

    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  return (
    <div className="header-menu" ref={ref}>
      <button
        type="button"
        className="browse"
        aria-expanded={open}
        aria-controls={id}
        title={title ?? label}
        onClick={() => setOpen((value) => !value)}
      >
        {label} <span aria-hidden="true">▾</span>
      </button>
      {open && (
        // 항목을 고르면 닫는다. 메뉴 하나에서 두 가지를 연달아 고르는 경우는 없다.
        <div className="header-menu-panel" id={id} role="menu" onClick={() => setOpen(false)}>
          {children}
        </div>
      )}
    </div>
  )
}

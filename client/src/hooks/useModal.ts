import { useEffect, useRef, type RefObject } from 'react'

/**
 * 대화상자 하나를 다루는 훅. Esc로 닫고, Tab이 뒤 화면으로 새지 않게 가두고,
 * 닫을 때 원래 포커스로 돌려놓는다. 대화상자마다 따로 구현하면 어디는 되고 어디는 안 된다.
 *
 * 도움말이 "Esc가 열린 대화상자를 닫는다"고 적어 둔 이상 모든 대화상자가 같아야 한다.
 */
export function useModal<T extends HTMLElement>(
  onClose: () => void,
  /** 열자마자 포커스를 둘 요소. 없으면 안쪽 첫 포커스 가능 요소로 간다. */
  initialFocus?: RefObject<HTMLElement | null>,
) {
  const ref = useRef<T>(null)
  const closeRef = useRef(onClose)
  closeRef.current = onClose
  const initialFocusRef = useRef(initialFocus)
  initialFocusRef.current = initialFocus

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    const container = ref.current
    // 열자마자 안쪽으로 포커스를 옮긴다. 옮기지 않으면 Tab이 뒤 화면부터 훑는다.
    const focusable = () => [...(container?.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]),' +
      ' textarea:not([disabled]), details, summary, [tabindex]:not([tabindex="-1"])',
    ) ?? [])].filter((item) => item.offsetParent !== null)

    const first = initialFocusRef.current?.current ?? focusable()[0]
    first?.focus()

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation()
        closeRef.current()
        return
      }

      if (event.key !== 'Tab') return
      const items = focusable()
      if (items.length === 0) return
      const first = items[0]
      const last = items[items.length - 1]
      const active = document.activeElement
      if (event.shiftKey && (active === first || !container?.contains(active))) {
        event.preventDefault()
        last.focus()
        return
      }

      if (!event.shiftKey && active === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown, true)
    return () => {
      document.removeEventListener('keydown', onKeyDown, true)
      previous?.focus?.()
    }
  }, [])

  return ref
}

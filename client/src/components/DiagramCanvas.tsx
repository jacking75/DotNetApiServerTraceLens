import { useEffect, useRef, type ReactNode, type RefObject } from 'react'
import type { PlacedPackage } from '../graph/packageLayout'

export interface CanvasPath {
  id: string
  d: string
  className: string
  marker: string
}

export const ZOOM_MIN = 0.25
export const ZOOM_MAX = 2
const ZOOM_STEP = 0.1

export const clampZoom = (value: number) => Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, Number(value.toFixed(2))))

interface DiagramCanvasProps {
  shellRef: RefObject<HTMLDivElement>
  width: number
  height: number
  zoom: number
  paths: CanvasPath[]
  packages?: PlacedPackage[]
  packageLabel?: (namespace: string) => string
  children: ReactNode
  emptyMessage?: string
  /** 이 좌표가 바뀔 때마다 그 위치로 스크롤한다(네임스페이스 이름 클릭). */
  scrollTarget?: { x: number; y: number; token: number } | null
  ariaLabel: string
  /** 주어지면 드래그 스크롤·Ctrl+휠·단축키(Ctrl +/-)로 줌을 바꿀 수 있다. */
  onZoomChange?: (zoom: number) => void
  /** 내부 "맞춤" 버튼과 Ctrl+0 단축키에 연결한다. */
  onFitZoom?: () => void
}

/**
 * 배치 결과(박스 + 선)를 그리는 공용 캔버스.
 * 박스는 절대 위치 DOM으로, 선은 그 아래 SVG 한 장으로 그리고, 줌은 바깥 래퍼의 transform으로 준다.
 * 드래그로 화면을 옮기고(빈 배경에서 시작할 때만), 캔버스 위에서 Ctrl+휠이나 Ctrl+/-/0으로 줌을 바꿀 수 있다.
 */
export function DiagramCanvas({
  shellRef,
  width,
  height,
  zoom,
  paths,
  packages,
  packageLabel,
  children,
  emptyMessage,
  scrollTarget,
  ariaLabel,
  onZoomChange,
  onFitZoom,
}: DiagramCanvasProps) {
  const token = scrollTarget?.token
  useEffect(() => {
    if (!scrollTarget || !shellRef.current) return
    shellRef.current.scrollLeft = Math.max(0, scrollTarget.x * zoom - 24)
    shellRef.current.scrollTop = Math.max(0, scrollTarget.y * zoom - 24)
    // token이 바뀔 때만 스크롤한다. 좌표는 같아도 다시 눌렀으면 이동해야 한다.
  }, [token]) // eslint-disable-line react-hooks/exhaustive-deps

  const hoveringRef = useRef(false)
  const dragRef = useRef<{ startX: number; startY: number; scrollLeft: number; scrollTop: number; moved: boolean } | null>(null)

  // Ctrl/Cmd+휠 줌. 브라우저 페이지 확대를 막으려면 passive:false 네이티브 리스너가 필요하다(React onWheel은 preventDefault가 안 먹는다).
  useEffect(() => {
    const shell = shellRef.current
    if (!shell || !onZoomChange) return
    const handleWheel = (event: WheelEvent) => {
      if (!(event.ctrlKey || event.metaKey)) return
      event.preventDefault()
      onZoomChange(clampZoom(zoom + (event.deltaY > 0 ? -ZOOM_STEP : ZOOM_STEP)))
    }
    shell.addEventListener('wheel', handleWheel, { passive: false })
    return () => shell.removeEventListener('wheel', handleWheel)
  }, [shellRef, zoom, onZoomChange])

  // 캔버스 위에 마우스가 있을 때만 Ctrl +/-/0을 가로챈다.
  useEffect(() => {
    if (!onZoomChange) return
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!hoveringRef.current || !(event.ctrlKey || event.metaKey)) return
      if (event.key === '+' || event.key === '=') {
        event.preventDefault()
        onZoomChange(clampZoom(zoom + ZOOM_STEP))
      } else if (event.key === '-' || event.key === '_') {
        event.preventDefault()
        onZoomChange(clampZoom(zoom - ZOOM_STEP))
      } else if (event.key === '0' && onFitZoom) {
        event.preventDefault()
        onFitZoom()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [zoom, onZoomChange, onFitZoom])

  const handlePointerDown = (event: React.PointerEvent<HTMLDivElement>) => {
    const shell = shellRef.current
    if (!shell || event.button !== 0) return
    const target = event.target as HTMLElement
    if (target.closest('.uml, .dg-node')) return // 박스 위에서는 기존 클릭 선택을 그대로 둔다.
    dragRef.current = { startX: event.clientX, startY: event.clientY, scrollLeft: shell.scrollLeft, scrollTop: shell.scrollTop, moved: false }
    shell.setPointerCapture(event.pointerId)
  }

  const handlePointerMove = (event: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current
    const shell = shellRef.current
    if (!drag || !shell) return
    const dx = event.clientX - drag.startX
    const dy = event.clientY - drag.startY
    if (!drag.moved && Math.hypot(dx, dy) < 4) return
    drag.moved = true
    shell.classList.add('panning')
    shell.scrollLeft = drag.scrollLeft - dx
    shell.scrollTop = drag.scrollTop - dy
  }

  const endDrag = (event: React.PointerEvent<HTMLDivElement>) => {
    const shell = shellRef.current
    dragRef.current = null
    shell?.classList.remove('panning')
    if (shell?.hasPointerCapture(event.pointerId)) shell.releasePointerCapture(event.pointerId)
  }

  return (
    <div className="diagram-wrap">
      <div
        className="graph-shell"
        ref={shellRef}
        aria-label={ariaLabel}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
        onMouseEnter={() => { hoveringRef.current = true }}
        onMouseLeave={() => { hoveringRef.current = false }}
      >
        {/* 화살촉 정의는 캔버스가 비어도 범례가 참조하므로 항상 둔다. */}
        <svg width={0} height={0} className="dg-defs" aria-hidden="true"><DiagramMarkers /></svg>
        {emptyMessage && <div className="panel-message canvas-empty">{emptyMessage}</div>}
        {!emptyMessage && (
          <div className="dg-scale" style={{ transform: `scale(${zoom})`, width, height }}>
            <div className="dg-root" style={{ width, height }}>
              {packages?.map((item) => (
                <div
                  key={item.namespace}
                  className="pkg"
                  style={{ left: item.x, top: item.y, width: item.w, height: item.h }}
                >
                  <div className="pkg-title">{packageLabel ? packageLabel(item.namespace) : item.namespace}</div>
                </div>
              ))}
              <svg className="dg-edges" width={width} height={height}>
                {paths.map((path) => (
                  <path key={path.id} d={path.d} className={path.className} fill="none" markerEnd={`url(#${path.marker})`} />
                ))}
              </svg>
              {children}
            </div>
          </div>
        )}
      </div>
      {!emptyMessage && onZoomChange && (
        <ZoomControls zoom={zoom} onZoomChange={onZoomChange} onFitZoom={onFitZoom} />
      )}
    </div>
  )
}

interface ZoomControlsProps {
  zoom: number
  onZoomChange: (zoom: number) => void
  onFitZoom?: () => void
}

function ZoomControls({ zoom, onZoomChange, onFitZoom }: ZoomControlsProps) {
  return (
    <div className="zoom-controls">
      <button type="button" onClick={() => onZoomChange(clampZoom(zoom - ZOOM_STEP))} title="축소 (Ctrl -)" aria-label="축소">−</button>
      <button type="button" className="zoom-pct" onClick={() => onZoomChange(1)} title="100%로">{Math.round(zoom * 100)}%</button>
      <button type="button" onClick={() => onZoomChange(clampZoom(zoom + ZOOM_STEP))} title="확대 (Ctrl +)" aria-label="확대">+</button>
      {onFitZoom && (
        <>
          <span className="zoom-sep" />
          <button type="button" onClick={onFitZoom} title="맞춤 (Ctrl 0)">맞춤</button>
        </>
      )}
    </div>
  )
}

/** 관계별 화살촉. 상속·구현은 빈 삼각형, 연관은 열린 화살표, 의존은 얇은 열린 화살표다. */
function DiagramMarkers() {
  return (
    <defs>
      <marker id="mk-arrow" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto">
        <path d="M0 0 L8 4 L0 8 z" fill="#46566e" />
      </marker>
      <marker id="mk-arrow-active" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto">
        <path d="M0 0 L8 4 L0 8 z" fill="#79d4ff" />
      </marker>
      <marker id="mk-triangle" viewBox="0 0 12 12" refX="11" refY="6" markerWidth="11" markerHeight="11" orient="auto">
        <path d="M1 1 L11 6 L1 11 z" fill="#141d2c" stroke="#9fb4d0" strokeWidth="1.2" />
      </marker>
      <marker id="mk-triangle-active" viewBox="0 0 12 12" refX="11" refY="6" markerWidth="11" markerHeight="11" orient="auto">
        <path d="M1 1 L11 6 L1 11 z" fill="#141d2c" stroke="#79d4ff" strokeWidth="1.4" />
      </marker>
      <marker id="mk-open" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto">
        <path d="M1 1 L9 5 L1 9" fill="none" stroke="#6d90b8" strokeWidth="1.4" />
      </marker>
      <marker id="mk-open-active" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto">
        <path d="M1 1 L9 5 L1 9" fill="none" stroke="#79d4ff" strokeWidth="1.6" />
      </marker>
      <marker id="mk-depend" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto">
        <path d="M1 1 L9 5 L1 9" fill="none" stroke="#4a5a72" strokeWidth="1.3" />
      </marker>
    </defs>
  )
}

import { useEffect, type ReactNode, type RefObject } from 'react'
import type { PlacedPackage } from '../graph/packageLayout'

export interface CanvasPath {
  id: string
  d: string
  className: string
  marker: string
}

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
}

/**
 * 배치 결과(박스 + 선)를 그리는 공용 캔버스.
 * 박스는 절대 위치 DOM으로, 선은 그 아래 SVG 한 장으로 그리고, 줌은 바깥 래퍼의 transform으로 준다.
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
}: DiagramCanvasProps) {
  const token = scrollTarget?.token
  useEffect(() => {
    if (!scrollTarget || !shellRef.current) return
    shellRef.current.scrollLeft = Math.max(0, scrollTarget.x * zoom - 24)
    shellRef.current.scrollTop = Math.max(0, scrollTarget.y * zoom - 24)
    // token이 바뀔 때만 스크롤한다. 좌표는 같아도 다시 눌렀으면 이동해야 한다.
  }, [token]) // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div className="graph-shell" ref={shellRef} aria-label={ariaLabel}>
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

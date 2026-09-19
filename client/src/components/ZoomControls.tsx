import { useI18n } from '../i18n/I18nProvider'

export const ZOOM_MIN = 0.25
export const ZOOM_MAX = 2
export const ZOOM_STEP = 0.1

export const clampZoom = (value: number) => Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, Number(value.toFixed(2))))

interface ZoomControlsProps {
  zoom: number
  onZoomChange: (zoom: number) => void
  onFitZoom?: () => void
}

/**
 * 캔버스 위에 띄우는 줌·맞춤 컨트롤. 세 캔버스(클래스 다이어그램·전체 흐름도·호출 흐름)가 같은 것을 쓴다.
 * 보기마다 컨트롤이 있거나 없으면 "이 화면에는 맞춤이 없나" 하고 찾게 된다.
 */
export function ZoomControls({ zoom, onZoomChange, onFitZoom }: ZoomControlsProps) {
  const { t } = useI18n()
  return (
    <div className="zoom-controls">
      <button
        type="button"
        onClick={() => onZoomChange(clampZoom(zoom - ZOOM_STEP))}
        title={t('canvas.zoomOutTitle')}
        aria-label={t('canvas.zoomOut')}
      >
        −
      </button>
      <button type="button" className="zoom-pct" onClick={() => onZoomChange(1)} title={t('canvas.zoomReset')}>
        {Math.round(zoom * 100)}%
      </button>
      <button
        type="button"
        onClick={() => onZoomChange(clampZoom(zoom + ZOOM_STEP))}
        title={t('canvas.zoomInTitle')}
        aria-label={t('canvas.zoomIn')}
      >
        +
      </button>
      {onFitZoom && (
        <>
          <span className="zoom-sep" />
          <button type="button" onClick={onFitZoom} title={t('canvas.fitTitle')}>{t('canvas.fit')}</button>
        </>
      )}
    </div>
  )
}

import { useState } from 'react'
import { getImpact, ApiError } from '../api/client'
import { translateError, useI18n } from '../i18n/I18nProvider'
import { confidenceLabel, resolutionLabel } from '../i18n/labels'
import { SourceLink, type SourceLocation } from './SourceLink'
import type { ImpactResult } from '../types/context'

interface ImpactPanelProps {
  nodeId: string | null
  snapshotId: string
  /** 진입점을 고르면 기존 호출 흐름 보기로 이동한다. 여러 진입점을 한 그래프로 펼치지 않는다. */
  onOpenEntryPoint: (handlerNodeId: string, targetNodeId: string) => void
  /** `파일:줄`을 누르면 코드 보기를 그 위치로 옮긴다. */
  onOpenSource: (location: SourceLocation) => void
}

/**
 * 선택한 메서드를 바꾸기 전에 영향을 받을 수 있는 API를 찾는다.
 * 결과는 호출 관계에서 도출한 <b>후보</b>이며, 호출자가 없다고 삭제해도 안전하다는 뜻이 아니다.
 */
export function ImpactPanel({ nodeId, snapshotId, onOpenEntryPoint, onOpenSource }: ImpactPanelProps) {
  const { t } = useI18n()
  const [result, setResult] = useState<ImpactResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const run = async () => {
    if (!nodeId) return
    setBusy(true)
    setError(null)
    try {
      setResult(await getImpact([nodeId], snapshotId || undefined))
    } catch (reason) {
      setResult(null)
      setError(reason instanceof ApiError
        ? translateError(t, reason.code, reason.message)
        : t('impact.failed'))
    } finally {
      setBusy(false)
    }
  }

  if (!nodeId) return null

  return (
    <section className="impact-panel" id="tracelens-impact-panel">
      <header>
        <span>{t('impact.title')}</span>
        <button type="button" onClick={() => void run()} disabled={busy}>
          {busy ? t('impact.running') : t('impact.run')}
        </button>
      </header>

      {error && <div className="panel-message tight">{error}</div>}

      {result && (
        <div className="impact-body">
          <div className="hint">{t('impact.count', { count: result.endpoints.length })}</div>
          {result.truncated && <div className="hint warn">{t('impact.truncated')}</div>}

          {result.endpoints.length === 0 && <div className="panel-message tight">{t('impact.empty')}</div>}

          <ul className="impact-list">
            {result.endpoints.map((endpoint) => (
              <li key={endpoint.endpointId}>
                <button
                  type="button"
                  className="impact-endpoint"
                  onClick={() => onOpenEntryPoint(endpoint.handlerNodeId, nodeId)}
                  title={t('impact.open')}
                >
                  <span className={`http-method ${endpoint.httpMethod.toLowerCase()}`}>{endpoint.httpMethod}</span>
                  <span className="route">{endpoint.route}</span>
                  <span className="chip small">{t('impact.distance', { count: endpoint.distance })}</span>
                  <span className="chip small">{confidenceLabel(endpoint.confidence, t)}</span>
                </button>
                <ol className="impact-path">
                  {endpoint.path.map((step) => (
                    <li key={step.nodeId}>
                      {step.displayName} <SourceLink file={step.file} line={step.startLine} onOpen={onOpenSource} />
                      <span className="chip small">{resolutionLabel(step.resolution, t)}</span>
                    </li>
                  ))}
                </ol>
              </li>
            ))}
          </ul>

          {result.testCandidates.length > 0 && (
            <details>
              <summary>{t('impact.tests')} ({result.testCandidates.length})</summary>
              <ul>
                {result.testCandidates.map((item) => (
                  <li key={item.nodeId}>
                    {item.displayName} <SourceLink file={item.file} line={item.startLine} onOpen={onOpenSource} />
                    <span className="chip small">
                      {item.evidence === 'reached' ? t('impact.testEvidenceReached') : t('impact.testEvidenceName')}
                    </span>
                  </li>
                ))}
              </ul>
            </details>
          )}

          <details>
            <summary>{t('impact.limitations')}</summary>
            <ul className="context-limitations">
              {result.limitations.map((item) => <li key={item}>{item}</li>)}
              {result.truncationReasons.map((item) => <li key={item}>{item}</li>)}
            </ul>
          </details>
        </div>
      )}
    </section>
  )
}

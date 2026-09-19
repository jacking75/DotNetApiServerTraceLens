import { useEffect, useState } from 'react'
import { getImpact, ApiError } from '../api/client'
import { translateError, useI18n } from '../i18n/I18nProvider'
import { confidenceLabel, resolutionLabel } from '../i18n/labels'
import { SourceLink, type SourceLocation } from './SourceLink'
import type { ImpactResult } from '../types/context'

/** 영향 분석에 넣은 대상. 이름을 함께 들고 있어야 결과 머리에 "무엇의 결과인지"를 적을 수 있다. */
export interface ImpactTarget {
  nodeId: string
  label: string
}

interface ImpactPanelProps {
  /** 지금 선택한 메서드. "대상 추가"의 재료이자 단일 대상 실행의 기본값이다. */
  nodeId: string | null
  nodeLabel: string
  snapshotId: string
  /** 쌓아 둔 추가 대상. 비어 있으면 선택한 메서드 하나만 본다. */
  targets: ImpactTarget[]
  onChangeTargets: (targets: ImpactTarget[]) => void
  /** 결과는 App이 들고 있는다. 우측 열을 접었다 펴도 남아야 하기 때문이다. */
  result: ImpactResult | null
  onChangeResult: (result: ImpactResult | null) => void
  /** 진입점을 고르면 기존 호출 흐름 보기로 이동한다. 여러 진입점을 한 그래프로 펼치지 않는다. */
  onOpenEntryPoint: (handlerNodeId: string, targetNodeId: string) => void
  /** `파일:줄`을 누르면 코드 보기를 그 위치로 옮긴다. */
  onOpenSource: (location: SourceLocation) => void
  /** Shift+클릭·컨텍스트 메뉴로 에디터에서 연다. */
  onOpenInEditor: (location: SourceLocation) => void
  /** 결과 목록을 Markdown으로 복사한다. */
  onCopyMarkdown: (markdown: string) => void
}

/** 영향 분석의 기본 시간 제한(ms). 서버 상한은 60000이다. */
const defaultTimeoutMs = 5000

/**
 * 선택한 메서드를 바꾸기 전에 영향을 받을 수 있는 API를 찾는다.
 * 결과는 호출 관계에서 도출한 <b>후보</b>이며, 호출자가 없다고 삭제해도 안전하다는 뜻이 아니다.
 */
export function ImpactPanel({
  nodeId,
  nodeLabel,
  snapshotId,
  targets,
  onChangeTargets,
  result,
  onChangeResult,
  onOpenEntryPoint,
  onOpenSource,
  onOpenInEditor,
  onCopyMarkdown,
}: ImpactPanelProps) {
  const { t } = useI18n()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [timeoutMs, setTimeoutMs] = useState(defaultTimeoutMs)
  /** 이 결과가 무엇의 결과인지. 대상이 바뀌면 결과와 함께 비운다. */
  const [resultLabel, setResultLabel] = useState<string | null>(null)

  /**
   * 대상이 바뀌면 이전 결과를 버린다.
   * 옛 결과 + 새 nodeId로 진입점을 열면 존재하지 않는 경로를 그린다.
   */
  const targetKey = [nodeId ?? '', ...targets.map((item) => item.nodeId)].join('|')
  useEffect(() => {
    onChangeResult(null)
    setResultLabel(null)
    setError(null)
    // onChangeResult는 App이 useCallback으로 고정한다. 대상이 바뀔 때만 돌아야 한다.
  }, [targetKey]) // eslint-disable-line react-hooks/exhaustive-deps

  const allTargets: ImpactTarget[] = nodeId
    ? [{ nodeId, label: nodeLabel }, ...targets.filter((item) => item.nodeId !== nodeId)]
    : targets

  const run = async () => {
    if (allTargets.length === 0) return
    setBusy(true)
    setError(null)
    try {
      onChangeResult(await getImpact(allTargets.map((item) => item.nodeId), snapshotId || undefined, timeoutMs))
      setResultLabel(allTargets.map((item) => item.label).join(', '))
    } catch (reason) {
      onChangeResult(null)
      setResultLabel(null)
      setError(reason instanceof ApiError
        ? translateError(t, reason.code, reason.message)
        : t('impact.failed'))
    } finally {
      setBusy(false)
    }
  }

  const copyMarkdown = () => {
    if (!result) return
    const lines = [
      `## ${t('impact.title')} — ${resultLabel ?? ''}`,
      '',
      ...result.endpoints.map((endpoint) =>
        `- \`${endpoint.httpMethod} ${endpoint.route}\` · ${t('impact.distance', { count: endpoint.distance })}` +
        ` · ${confidenceLabel(endpoint.confidence, t)}`),
      '',
      ...result.limitations.map((item) => `> ${item}`),
    ]
    onCopyMarkdown(lines.join('\n'))
  }

  return (
    <section className="impact-panel" id="tracelens-impact-panel">
      <header>
        <span>{t('impact.title')}</span>
        <button type="button" onClick={() => void run()} disabled={busy || allTargets.length === 0}>
          {busy ? t('impact.running') : t('impact.run')}
        </button>
      </header>

      {/* 패널이 나타났다 사라지면 "있었는데 없다"가 된다. 항상 그리고 비활성 사유를 말한다. */}
      {!nodeId && targets.length === 0 && <div className="panel-message tight">{t('impact.needMethod')}</div>}

      <div className="impact-targets">
        {allTargets.map((target) => (
          <span key={target.nodeId} className="chip small" title={target.nodeId}>
            {target.label}
            {targets.some((item) => item.nodeId === target.nodeId) && (
              <button
                type="button"
                className="link-button"
                aria-label={t('impact.removeTarget')}
                onClick={() => onChangeTargets(targets.filter((item) => item.nodeId !== target.nodeId))}
              >
                ×
              </button>
            )}
          </span>
        ))}
        {nodeId && !targets.some((item) => item.nodeId === nodeId) && (
          <button
            type="button"
            className="link-button"
            onClick={() => onChangeTargets([...targets, { nodeId, label: nodeLabel }])}
          >
            {t('impact.addTarget')}
          </button>
        )}
        <label className="impact-timeout">
          {t('impact.timeout')}
          <select value={timeoutMs} onChange={(event) => setTimeoutMs(Number(event.target.value))}>
            {[5000, 15000, 60000].map((value) => (
              <option key={value} value={value}>{value / 1000}s</option>
            ))}
          </select>
        </label>
      </div>

      {error && <div className="panel-message tight">{error}</div>}

      {result && (
        <div className="impact-body">
          {resultLabel && <div className="hint"><b>{t('impact.resultFor', { target: resultLabel })}</b></div>}
          <div className="hint">{t('impact.count', { count: result.endpoints.length })}</div>
          {result.truncated && <div className="hint warn">{t('impact.truncated')}</div>}
          {result.unreachableTargets.length > 0 && (
            <div className="hint warn">
              {t('impact.unreachable', { count: result.unreachableTargets.length })}
              <span className="sub">{result.unreachableTargets.join(', ')}</span>
            </div>
          )}

          {result.endpoints.length === 0 && <div className="panel-message tight">{t('impact.empty')}</div>}

          <ul className="impact-list">
            {result.endpoints.map((endpoint) => (
              <li key={endpoint.endpointId}>
                <button
                  type="button"
                  className="impact-endpoint"
                  onClick={() => onOpenEntryPoint(
                    endpoint.handlerNodeId,
                    endpoint.path[endpoint.path.length - 1]?.nodeId ?? nodeId ?? '')}
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
                      {step.displayName}{' '}
                      <SourceLink
                        file={step.file}
                        line={step.startLine}
                        onOpen={onOpenSource}
                        onOpenInEditor={onOpenInEditor}
                      />
                      <span className="chip small">{resolutionLabel(step.resolution, t)}</span>
                    </li>
                  ))}
                </ol>
              </li>
            ))}
          </ul>

          {(result.dataWrites ?? []).length > 0 && (
            <details>
              <summary>{t('impact.dataWrites', { count: (result.dataWrites ?? []).length })}</summary>
              <ul>
                {(result.dataWrites ?? []).map((item) => (
                  <li key={`${item.target}-${item.file}-${item.line}`}>
                    <b>{item.target || item.dataSource}</b> · {item.operation}{' '}
                    <SourceLink
                      file={item.file}
                      line={item.line}
                      onOpen={onOpenSource}
                      onOpenInEditor={onOpenInEditor}
                    />
                    <span className={`chip small${item.confidence === 'Estimated' ? ' warn' : ''}`}>
                      {confidenceLabel(item.confidence, t)}
                    </span>
                  </li>
                ))}
              </ul>
            </details>
          )}

          {result.testCandidates.length > 0 && (
            <details>
              <summary>{t('impact.tests')} ({result.testCandidates.length})</summary>
              <ul>
                {result.testCandidates.map((item) => (
                  <li key={item.nodeId}>
                    {item.displayName}{' '}
                    <SourceLink
                      file={item.file}
                      line={item.startLine}
                      onOpen={onOpenSource}
                      onOpenInEditor={onOpenInEditor}
                    />
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

          <button type="button" className="link-button" onClick={copyMarkdown}>
            {t('export.copyMarkdown')}
          </button>
        </div>
      )}
    </section>
  )
}

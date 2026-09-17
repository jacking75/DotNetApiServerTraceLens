import { useMemo } from 'react'
import { filterFindings } from '../graph/dataView'
import type { MiddlewareViewState } from '../state/viewState'
import type { MiddlewarePipelineDocument, MiddlewareStepInfo, Selection } from '../types/graph'
import { useI18n } from '../i18n/I18nProvider'
import { FindingTable, SeverityBadge } from './DataAccessView'

interface MiddlewareViewProps {
  pipeline: MiddlewarePipelineDocument
  view: MiddlewareViewState
  selection: Selection | null
  onSelect: (selection: Selection) => void
}

export function MiddlewareView({ pipeline, view, selection, onSelect }: MiddlewareViewProps) {
  const { t } = useI18n()
  const findings = useMemo(
    () => filterFindings(pipeline.findings, { severity: view.severity, onlyCertain: false }),
    [pipeline.findings, view.severity])
  const findingsByStep = useMemo(() => {
    const map = new Map<string, typeof pipeline.findings>()
    for (const finding of pipeline.findings) {
      if (!finding.stepId) continue
      const list = map.get(finding.stepId)
      if (list) list.push(finding)
      else map.set(finding.stepId, [finding])
    }

    return map
  }, [pipeline.findings])

  if (view.sub === 'check') {
    return <FindingTable findings={findings} selection={selection} onSelect={onSelect} />
  }

  if (view.sub === 'sc') {
    return <ShortCircuitView pipeline={pipeline} />
  }

  return (
    <div className="data-view">
      <div className="pipe-head">
        <h3>{t('mw.title')}</h3>
        <span className="sub">{t('mw.lead')}</span>
      </div>

      {pipeline.steps.map((step, index) => (
        <div key={step.id}>
          <div className="mwrow">
            <div className="mwnum">{step.order}</div>
            <button
              type="button"
              className={[
                'mw-card',
                step.kind !== 'Middleware' ? 'endpoint' : '',
                step.kind === 'EndpointRegistration' ? 'registration' : '',
                step.isDead ? 'dim' : '',
                findingsByStep.get(step.id)?.some((finding) => finding.severity === 'Critical') ? 'bad' : '',
                findingsByStep.get(step.id)?.some((finding) => finding.severity === 'Warning') ? 'warn' : '',
                selection?.type === 'middlewareStep' && selection.key === step.id ? 'selected' : '',
              ].filter(Boolean).join(' ')}
              onClick={() => onSelect({ type: 'middlewareStep', key: step.id })}
            >
              <div className="mw-top">
                <span className="mw-name">{step.name}</span>
                <span className="tag">{step.type === 'Custom' ? t('mw.custom') : t('mw.framework')}</span>
                {step.kind === 'EndpointRegistration' && <span className="tag">{t('mw.registration')}</span>}
                {step.kind === 'Terminal' && <span className="tag">{t('mw.terminal')}</span>}
                {step.condition && <span className="tag replica">{t('mw.conditional')}</span>}
                {step.isDead && <span className="sev warn">{t('mw.dead')}</span>}
                {findingsByStep.get(step.id)?.map((finding) => (
                  <span className={`sev ${finding.severity.toLowerCase()}`} key={finding.id}>{finding.ruleId}</span>
                ))}
                <span className="mw-reg">{step.registrationFile}:{step.registrationLine}</span>
              </div>
              {step.pre && (
                <div className="mw-line pre"><span className="dir">{t('mw.request')}</span><span>{step.pre}</span></div>
              )}
              {view.showPost && step.post && (
                <div className="mw-line post"><span className="dir">{t('mw.response')}</span><span>{step.post}</span></div>
              )}
              {(step.contextAccesses.length > 0 || step.storeAccesses.length > 0) && (
                <div className="mw-io">
                  {step.contextAccesses.map((access) => (
                    <span className={`io ${access.mode === 'Read' ? 'r' : 'w'}`} key={`${access.mode}${access.target}`}>
                      {access.mode === 'Read' ? t('mw.read') : t('mw.write')} {access.target}
                    </span>
                  ))}
                  {step.storeAccesses.map((access) => (
                    <span className="io store" key={access}>{access}</span>
                  ))}
                </div>
              )}
              {step.filters.length > 0 && (
                <div className="mw-filters">
                  {step.filters.map((filter) => (
                    <span key={filter.name}>└ {filter.name} <span className="dim">{filter.detail}</span></span>
                  ))}
                </div>
              )}
            </button>
            <div className="mw-branch">
              {step.shortCircuit && (
                <div className="branch">
                  <span className="code">{step.shortCircuit.statusCode}</span> {step.shortCircuit.condition}
                </div>
              )}
            </div>
          </div>
          {index < pipeline.steps.length - 1 && <div className="pipe-arrow">↓</div>}
        </div>
      ))}

      {pipeline.steps.some((step) => step.kind === 'EndpointRegistration') && (
        <>
          <div className="pipe-arrow">↓</div>
          <div className="mwrow">
            <div className="mwnum">·</div>
            <div className="mw-card endpoint">
              <div className="mw-top">
                <span className="mw-name">{t('mw.endpointExecution')}</span>
                <span className="tag">{t('mw.virtualStep')}</span>
              </div>
              <div className="mw-line pre">
                <span className="dir">{t('mw.request')}</span>
                <span>
                  {t('mw.endpointExecutionBody', {
                    names: pipeline.steps
                      .filter((step) => step.kind === 'EndpointRegistration')
                      .map((step) => step.name)
                      .join(', '),
                  })}
                </span>
              </div>
            </div>
            <div className="mw-branch" />
          </div>
        </>
      )}

      <div className="pipe-legend">
        <span><b className="pre-mark">{t('mw.request')}</b> {t('mw.legendPre')}</span>
        <span><b className="post-mark">{t('mw.response')}</b> {t('mw.legendPost')}</span>
        <span>
          <span className="io r">{t('mw.read')}</span>
          <span className="io w">{t('mw.write')}</span> {t('mw.legendIo')}
        </span>
      </div>
    </div>
  )
}

/** 끊기는 자리마다 "실행되지 않는 것"과 "그래도 실행되는 응답 후처리"를 나란히 보여준다. */
function ShortCircuitView({ pipeline }: { pipeline: MiddlewarePipelineDocument }) {
  const { t } = useI18n()
  const points = pipeline.steps
    .map((step, index) => ({ step, index }))
    .filter((item) => item.step.shortCircuit !== null && !item.step.isDead)

  if (points.length === 0) {
    return <div className="panel-message">{t('mw.noShortCircuit')}</div>
  }

  return (
    <div className="data-view">
      {points.map(({ step, index }) => {
        const skipped = pipeline.steps.slice(index + 1).filter((item) => !item.isDead)
        const stillRun = pipeline.steps.slice(0, index).filter((item) => item.post).reverse()
        return (
          <div className={`sc-card${step.shortCircuit!.statusCode.startsWith('4') || step.shortCircuit!.statusCode.startsWith('5') ? ' bad' : ''}`} key={step.id}>
            <div className="sc-head">
              <span className="code">{step.shortCircuit!.statusCode}</span>
              <span className="name">{step.name}</span>
              <span className="tag">{step.shortCircuit!.condition || t('mw.conditionUnknown')}</span>
              <span className="mw-reg">{step.registrationFile}:{step.registrationLine}</span>
            </div>
            <div className="sc-cols">
              <div className="sc-col skip">
                <h5>{t('mw.skipped')}</h5>
                <ul>
                  {skipped.length === 0 && <li>{t('mw.skippedNone')}</li>}
                  {skipped.map((item) => (
                    <li key={item.id}>{item.name}{item.kind !== 'Middleware' ? t('mw.allTheWay') : ''}</li>
                  ))}
                </ul>
              </div>
              <div className="sc-col">
                <h5>{t('mw.stillRun')}</h5>
                <ul>
                  {stillRun.length === 0 && <li>{t('mw.none')}</li>}
                  {stillRun.map((item) => <li key={item.id}>{item.name} — {item.post}</li>)}
                </ul>
              </div>
            </div>
            <div className="sc-note">
              {t('mw.shortCircuitNote', { status: step.shortCircuit!.statusCode })}
            </div>
          </div>
        )
      })}
    </div>
  )
}

/** 좌측 패널에서 쓰는 단계 한 줄. */
export function MiddlewareStepRow({
  step,
  selected,
  severity,
  onSelect,
}: {
  step: MiddlewareStepInfo
  selected: boolean
  severity: 'Critical' | 'Warning' | 'Info' | null
  onSelect: () => void
}) {
  const { t } = useI18n()
  return (
    <button
      type="button"
      className={`side-item${selected ? ' selected' : ''}`}
      style={step.isDead ? { opacity: 0.55 } : undefined}
      onClick={onSelect}
    >
      <span className="count" style={{ minWidth: 14 }}>{step.order}</span>
      <span className="grow">
        {step.name}
        <span className="sub">
          {step.type === 'Custom' ? t('mw.custom') : t('mw.framework')}
          {step.shortCircuit ? t('mw.shortCircuitTag', { status: step.shortCircuit.statusCode }) : ''}
          {step.post ? t('mw.hasPost') : ''}
        </span>
      </span>
      {severity && <SeverityBadge severity={severity} />}
    </button>
  )
}

import { useMemo } from 'react'
import { filterFindings } from '../graph/dataView'
import type { MiddlewareViewState } from '../state/viewState'
import type { MiddlewarePipelineDocument, MiddlewareStepInfo, Selection } from '../types/graph'
import { FindingTable, SeverityBadge } from './DataAccessView'

interface MiddlewareViewProps {
  pipeline: MiddlewarePipelineDocument
  view: MiddlewareViewState
  selection: Selection | null
  onSelect: (selection: Selection) => void
}

export function MiddlewareView({ pipeline, view, selection, onSelect }: MiddlewareViewProps) {
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
        <h3>요청 파이프라인</h3>
        <span className="sub">
          위에서 아래로 요청, 아래에서 위로 응답. 오른쪽 빨간 가지는 그 자리에서 끊길 수 있다는 뜻이다.
        </span>
      </div>

      {pipeline.steps.map((step, index) => (
        <div key={step.id}>
          <div className="mwrow">
            <div className="mwnum">{step.order}</div>
            <button
              type="button"
              className={[
                'mw-card',
                step.isTerminal ? 'endpoint' : '',
                step.isDead ? 'dim' : '',
                findingsByStep.get(step.id)?.some((finding) => finding.severity === 'Critical') ? 'bad' : '',
                findingsByStep.get(step.id)?.some((finding) => finding.severity === 'Warning') ? 'warn' : '',
                selection?.type === 'middlewareStep' && selection.key === step.id ? 'selected' : '',
              ].filter(Boolean).join(' ')}
              onClick={() => onSelect({ type: 'middlewareStep', key: step.id })}
            >
              <div className="mw-top">
                <span className="mw-name">{step.name}</span>
                <span className="tag">{step.type === 'Custom' ? '직접 작성' : '프레임워크'}</span>
                {step.condition && <span className="tag replica">조건부</span>}
                {step.isDead && <span className="sev warn">실행되지 않음</span>}
                {findingsByStep.get(step.id)?.map((finding) => (
                  <span className={`sev ${finding.severity.toLowerCase()}`} key={finding.id}>{finding.ruleId}</span>
                ))}
                <span className="mw-reg">{step.registrationFile}:{step.registrationLine}</span>
              </div>
              {step.pre && (
                <div className="mw-line pre"><span className="dir">요청 →</span><span>{step.pre}</span></div>
              )}
              {view.showPost && step.post && (
                <div className="mw-line post"><span className="dir">← 응답</span><span>{step.post}</span></div>
              )}
              {(step.contextAccesses.length > 0 || step.storeAccesses.length > 0) && (
                <div className="mw-io">
                  {step.contextAccesses.map((access) => (
                    <span className={`io ${access.mode === 'Read' ? 'r' : 'w'}`} key={`${access.mode}${access.target}`}>
                      {access.mode === 'Read' ? '읽기' : '쓰기'} {access.target}
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

      <div className="pipe-legend">
        <span><b className="pre-mark">요청 →</b> next 호출 전에 하는 일</span>
        <span><b className="post-mark">← 응답</b> next 이후, 응답이 돌아올 때 하는 일</span>
        <span><span className="io r">읽기</span><span className="io w">쓰기</span> HttpContext 접근</span>
      </div>
    </div>
  )
}

/** 끊기는 자리마다 "실행되지 않는 것"과 "그래도 실행되는 응답 후처리"를 나란히 보여준다. */
function ShortCircuitView({ pipeline }: { pipeline: MiddlewarePipelineDocument }) {
  const points = pipeline.steps
    .map((step, index) => ({ step, index }))
    .filter((item) => item.step.shortCircuit !== null && !item.step.isDead)

  if (points.length === 0) {
    return <div className="panel-message">요청을 끊는 자리를 찾지 못했다.</div>
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
              <span className="tag">{step.shortCircuit!.condition || '조건을 읽지 못했다'}</span>
              <span className="mw-reg">{step.registrationFile}:{step.registrationLine}</span>
            </div>
            <div className="sc-cols">
              <div className="sc-col skip">
                <h5>실행되지 않는 것</h5>
                <ul>
                  {skipped.length === 0 && <li>없음 — 마지막 단계다</li>}
                  {skipped.map((item) => (
                    <li key={item.id}>{item.name}{item.isTerminal ? ' (액션까지 전부)' : ''}</li>
                  ))}
                </ul>
              </div>
              <div className="sc-col">
                <h5>그래도 실행되는 것 (응답 경로)</h5>
                <ul>
                  {stillRun.length === 0 && <li>없음</li>}
                  {stillRun.map((item) => <li key={item.id}>{item.name} — {item.post}</li>)}
                </ul>
              </div>
            </div>
            <div className="sc-note">
              여기서 끊기면 상태 코드 {step.shortCircuit!.statusCode}로 응답이 만들어지고, 위 목록의 응답 처리를 거쳐 돌아간다.
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
          {step.type === 'Custom' ? '직접 작성' : '프레임워크'}
          {step.shortCircuit ? ` · 단락 ${step.shortCircuit.statusCode}` : ''}
          {step.post ? ' · 응답 처리 있음' : ''}
        </span>
      </span>
      {severity && <SeverityBadge severity={severity} />}
    </button>
  )
}

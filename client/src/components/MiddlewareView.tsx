import { useMemo, useState } from 'react'
import { filterFindings } from '../graph/dataView'
import type { MiddlewareViewState } from '../state/viewState'
import type {
  Finding,
  FindingSeverity,
  MiddlewarePipelineDocument,
  MiddlewareStepInfo,
  MvcFilterInfo,
  Selection,
} from '../types/graph'
import { useI18n } from '../i18n/I18nProvider'
import { FindingTable, ruleName, SeverityBadge } from './DataAccessView'
import { HelpLink, type GlossaryTerm } from './HelpDialog'
import { ProjectPicker } from './ProjectPicker'
import { SourceLink, type SourceLocation } from './SourceLink'

interface MiddlewareViewProps {
  pipeline: MiddlewarePipelineDocument
  view: MiddlewareViewState
  selection: Selection | null
  onSelect: (selection: Selection) => void
  onExplain: (term: GlossaryTerm) => void
  onOpenSource: (location: SourceLocation) => void
  onOpenInEditor: (location: SourceLocation) => void
  projectKeys: string[]
  projectKey: string | null
  onChangeProject: (key: string) => void
}

/** 파이프라인 한 줄. 연속된 엔드포인트 등록은 한 묶음으로 접는다. */
export type PipelineRow =
  | { kind: 'step'; step: MiddlewareStepInfo }
  | { kind: 'group'; steps: MiddlewareStepInfo[] }

/**
 * 연속된 엔드포인트 등록을 한 묶음으로 접는다.
 * `MapControllers`·`MapGroup`·`MapGet` 50개가 각각 전체 카드로 그려지면 파이프라인이 50장이 된다.
 * 한 개짜리 묶음은 접는 의미가 없어 그대로 카드로 둔다.
 */
export function foldEndpointRegistrations(steps: readonly MiddlewareStepInfo[]): PipelineRow[] {
  const result: PipelineRow[] = []
  for (const step of steps) {
    if (step.kind !== 'EndpointRegistration') {
      result.push({ kind: 'step', step })
      continue
    }

    const last = result[result.length - 1]
    if (last?.kind === 'group') last.steps.push(step)
    else result.push({ kind: 'group', steps: [step] })
  }

  return result.map((item) =>
    item.kind === 'group' && item.steps.length === 1 ? { kind: 'step' as const, step: item.steps[0] } : item)
}

export function MiddlewareView({
  pipeline,
  view,
  selection,
  onSelect,
  onExplain,
  onOpenSource,
  onOpenInEditor,
  projectKeys,
  projectKey,
  onChangeProject,
}: MiddlewareViewProps) {
  const { t, lang } = useI18n()
  /** 펼쳐 둔 확장 메서드 단계. 기본은 접힘이라 파이프라인 한 줄이 한 등록으로 읽힌다. */
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  /** 펼쳐 둔 엔드포인트 등록 묶음. 묶음의 첫 단계 ID로 구분한다. */
  const [openGroups, setOpenGroups] = useState<ReadonlySet<string>>(() => new Set())
  const childCount = (stepId: string) => pipeline.steps.filter((item) => item.parentId === stepId).length
  const visibleSteps = pipeline.steps.filter((step) => !step.parentId || expanded.has(step.parentId))
  const rows = useMemo(() => foldEndpointRegistrations(visibleSteps), [visibleSteps])
  const findings = useMemo(
    () => filterFindings(pipeline.findings, { severity: view.severity, onlyCertain: false }),
    [pipeline.findings, view.severity])
  const findingsByStep = useMemo(() => {
    const map = new Map<string, Finding[]>()
    for (const finding of pipeline.findings) {
      if (!finding.stepId) continue
      const list = map.get(finding.stepId)
      if (list) list.push(finding)
      else map.set(finding.stepId, [finding])
    }

    return map
  }, [pipeline.findings])

  if (view.sub === 'check') {
    return <FindingTable findings={findings} rules={pipeline.rules} selection={selection} onSelect={onSelect} />
  }

  if (view.sub === 'sc') {
    return <ShortCircuitView pipeline={pipeline} onExplain={onExplain} />
  }

  const toggleGroup = (id: string) => setOpenGroups((current) => {
    const next = new Set(current)
    if (!next.delete(id)) next.add(id)
    return next
  })

  return (
    <div className="data-view">
      <div className="pipe-head">
        <h3>{t('mw.title')}</h3>
        <span className="sub">{t('mw.lead')}</span>
        <ProjectPicker projectKeys={projectKeys} projectKey={projectKey} onChange={onChangeProject} />
      </div>

      {rows.map((row, index) => (
        <div key={row.kind === 'group' ? `group-${row.steps[0].id}` : row.step.id}>
          {row.kind === 'group'
            ? (
              <>
                <div className="mwrow">
                  <div className="mwnum">{row.steps[0].order}</div>
                  <button
                    type="button"
                    className="mw-card endpoint registration"
                    aria-expanded={openGroups.has(row.steps[0].id)}
                    onClick={() => toggleGroup(row.steps[0].id)}
                  >
                    <div className="mw-top">
                      <span className="mw-name">{t('mw.endpointGroup', { count: row.steps.length })}</span>
                      <span className="tag">
                        {openGroups.has(row.steps[0].id) ? t('mw.collapseGroup') : t('mw.expandGroup')}
                      </span>
                    </div>
                    <div className="mw-line pre">
                      <span className="dir">{t('mw.request')}</span>
                      <span>{row.steps.map((item) => item.name).join(', ')}</span>
                    </div>
                  </button>
                  <div className="mw-branch" />
                </div>
                {openGroups.has(row.steps[0].id) && (
                  <ul className="mw-group-list">
                    {row.steps.map((item) => (
                      <li key={item.id}>
                        <button
                          type="button"
                          className="link-button"
                          onClick={() => onSelect({ type: 'middlewareStep', key: item.id })}
                        >
                          {item.order}. {item.name}
                        </button>{' '}
                        <SourceLink
                          file={item.registrationFile}
                          line={item.registrationLine}
                          onOpen={onOpenSource}
                          onOpenInEditor={onOpenInEditor}
                        />
                      </li>
                    ))}
                  </ul>
                )}
              </>
            )
            : (
              <div className={row.step.parentId ? 'mw-nested' : undefined}>
                <div className="mwrow">
                  <div className="mwnum">{row.step.order}</div>
                  <button
                    type="button"
                    className={[
                      'mw-card',
                      row.step.kind !== 'Middleware' ? 'endpoint' : '',
                      row.step.kind === 'EndpointRegistration' ? 'registration' : '',
                      row.step.isDead ? 'dim' : '',
                      findingsByStep.get(row.step.id)?.some((finding) => finding.severity === 'Critical') ? 'bad' : '',
                      findingsByStep.get(row.step.id)?.some((finding) => finding.severity === 'Warning') ? 'warn' : '',
                      selection?.type === 'middlewareStep' && selection.key === row.step.id ? 'selected' : '',
                    ].filter(Boolean).join(' ')}
                    onClick={() => {
                      onSelect({ type: 'middlewareStep', key: row.step.id })
                      if (row.step.type !== 'Extension') return
                      setExpanded((current) => {
                        const next = new Set(current)
                        if (!next.delete(row.step.id)) next.add(row.step.id)
                        return next
                      })
                    }}
                  >
                    <div className="mw-top">
                      <span className="mw-name">{row.step.name}</span>
                      <span className="tag">
                        {row.step.type === 'Custom'
                          ? t('mw.custom')
                          : row.step.type === 'Extension' ? t('mw.extension') : t('mw.framework')}
                      </span>
                      {row.step.type === 'Extension' && childCount(row.step.id) > 0 && (
                        <span className="tag">
                          {expanded.has(row.step.id)
                            ? t('data.collapse')
                            : t('mw.expandChildren', { count: childCount(row.step.id) })}
                        </span>
                      )}
                      {row.step.kind === 'Terminal' && <span className="tag">{t('mw.terminal')}</span>}
                      {row.step.condition && <span className="tag replica">{t('mw.conditional')}</span>}
                      {row.step.isDead && <span className="sev warn">{t('mw.dead')}</span>}
                      {findingsByStep.get(row.step.id)?.map((finding) => (
                        <span
                          className={`sev ${finding.severity.toLowerCase()}`}
                          key={finding.id}
                          title={[finding.ruleId, ruleName(finding.ruleId, pipeline.rules, lang)]
                            .filter(Boolean).join(' · ')}
                        >
                          {finding.ruleId} {ruleName(finding.ruleId, pipeline.rules, lang)}
                        </span>
                      ))}
                    </div>
                    {row.step.pre && (
                      <div className="mw-line pre">
                        <span className="dir">{t('mw.request')}</span><span>{row.step.pre}</span>
                      </div>
                    )}
                    {view.showPost && row.step.post && (
                      <div className="mw-line post">
                        <span className="dir">{t('mw.response')}</span><span>{row.step.post}</span>
                      </div>
                    )}
                    {(row.step.contextAccesses.length > 0 || row.step.storeAccesses.length > 0) && (
                      <div className="mw-io">
                        {row.step.contextAccesses.map((access) => (
                          <span
                            className={`io ${access.mode === 'Read' ? 'r' : 'w'}`}
                            key={`${access.mode}${access.target}`}
                          >
                            {access.mode === 'Read' ? t('mw.read') : t('mw.write')} {access.target}
                          </span>
                        ))}
                        {row.step.storeAccesses.map((access) => (
                          <span className="io store" key={access}>{access}</span>
                        ))}
                      </div>
                    )}
                    {row.step.filters.length > 0 && (
                      <div className="mw-filters">
                        {row.step.filters.map((filter) => (
                          <span key={filter.name}>└ {filter.name} <span className="dim">{filter.detail}</span></span>
                        ))}
                      </div>
                    )}
                  </button>
                  <div className="mw-branch">
                    {row.step.shortCircuit && (
                      // 조건식이 길면 배지 밖으로 넘친다. 두 줄까지 줄바꿈하고 나머지는 툴팁에 둔다.
                      <div className="branch" title={row.step.shortCircuit.condition}>
                        <span className="code">{row.step.shortCircuit.statusCode}</span>
                        <span className="cond">{row.step.shortCircuit.condition}</span>
                      </div>
                    )}
                  </div>
                </div>
                {/* 등록 줄은 위 묶음 카드가 대신 그린다. `파일:줄`은 카드 밖에 둔다(버튼 안의 버튼을 피한다). */}
                <div className="mw-reg">
                  <SourceLink
                    file={row.step.registrationFile}
                    line={row.step.registrationLine}
                    onOpen={onOpenSource}
                    onOpenInEditor={onOpenInEditor}
                  />
                </div>
              </div>
            )}
          {index < rows.length - 1 && <div className="pipe-arrow">↓</div>}
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

      <FilterSection
        filters={pipeline.filters ?? []}
        selection={selection}
        onSelect={onSelect}
        onOpenSource={onOpenSource}
        onOpenInEditor={onOpenInEditor}
      />

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

/**
 * MVC 필터 절. 미들웨어 단계와 섞지 않고 파이프라인 아래에 전역 → 컨트롤러 → 액션 순으로 둔다.
 * 요청이 컨트롤러에 닿기 전에 무엇이 도는지가 실행 순서 보기의 가장 큰 빈칸이었다.
 */
function FilterSection({
  filters,
  selection,
  onSelect,
  onOpenSource,
  onOpenInEditor,
}: {
  filters: MvcFilterInfo[]
  selection: Selection | null
  onSelect: (selection: Selection) => void
  onOpenSource: (location: SourceLocation) => void
  onOpenInEditor: (location: SourceLocation) => void
}) {
  const { t } = useI18n()
  if (filters.length === 0) return null

  const order: Record<MvcFilterInfo['scope'], number> = { Global: 0, Controller: 1, Action: 2 }
  const sorted = [...filters].sort((left, right) =>
    order[left.scope] - order[right.scope] || (left.order ?? 0) - (right.order ?? 0))

  return (
    <div className="mw-filter-section">
      <div className="pipe-head">
        <h3>{t('mw.filters', { count: filters.length })}</h3>
        <span className="sub">{t('mw.filtersHint')}</span>
      </div>
      <ol className="mw-filter-list">
        {sorted.map((filter) => (
          <li key={filter.id}>
            <button
              type="button"
              className={`mw-card${selection?.type === 'mvcFilter' && selection.key === filter.id ? ' selected' : ''}`}
              onClick={() => onSelect({ type: 'mvcFilter', key: filter.id })}
            >
              <div className="mw-top">
                <span className="mw-name">{filter.name}</span>
                <span className="tag">{t(`mw.filterScope.${filter.scope}` as const)}</span>
                {filter.appliesTo && <span className="tag">{filter.appliesTo}</span>}
                {filter.confidence === 'Estimated' && <span className="sev warn">{t('data.estimated')}</span>}
              </div>
              {filter.pre && (
                <div className="mw-line pre"><span className="dir">{t('mw.request')}</span><span>{filter.pre}</span></div>
              )}
              {filter.post && (
                <div className="mw-line post">
                  <span className="dir">{t('mw.response')}</span><span>{filter.post}</span>
                </div>
              )}
            </button>
            <SourceLink
              file={filter.implementationFile ?? filter.registrationFile}
              line={filter.implementationFile ? filter.implementationLine : filter.registrationLine}
              onOpen={onOpenSource}
              onOpenInEditor={onOpenInEditor}
            />
          </li>
        ))}
      </ol>
    </div>
  )
}

/** 끊기는 자리마다 "실행되지 않는 것"과 "그래도 실행되는 응답 후처리"를 나란히 보여준다. */
function ShortCircuitView({
  pipeline,
  onExplain,
}: {
  pipeline: MiddlewarePipelineDocument
  onExplain: (term: GlossaryTerm) => void
}) {
  const { t } = useI18n()
  const points = pipeline.steps
    .map((step, index) => ({ step, index }))
    .filter((item) => item.step.shortCircuit !== null && !item.step.isDead)

  const heading = (
    <div className="view-intro">
      <h3>{t('sub.sc')}<HelpLink term="shortCircuit" onOpen={onExplain} /></h3>
    </div>
  )

  if (points.length === 0) {
    return <div className="data-view">{heading}<div className="panel-message">{t('mw.noShortCircuit')}</div></div>
  }

  return (
    <div className="data-view">
      {heading}
      {points.map(({ step, index }) => {
        const skipped = pipeline.steps.slice(index + 1).filter((item) => !item.isDead)
        const stillRun = pipeline.steps.slice(0, index).filter((item) => item.post).reverse()
        return (
          <div
            className={`sc-card${step.shortCircuit!.statusCode.startsWith('4') || step.shortCircuit!.statusCode.startsWith('5') ? ' bad' : ''}`}
            key={step.id}
          >
            <div className="sc-head">
              <span className="code">{step.shortCircuit!.statusCode}</span>
              <span className="name">{step.name}</span>
              <span className="tag" title={step.shortCircuit!.condition}>
                {step.shortCircuit!.condition || t('mw.conditionUnknown')}
              </span>
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
  severity: FindingSeverity | null
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

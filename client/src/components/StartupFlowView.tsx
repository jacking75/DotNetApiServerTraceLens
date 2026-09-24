import { useState, type CSSProperties } from 'react'
import { countSteps, startupStepKey, type StartupGroup } from '../graph/startupFlow'
import type { Selection, StartupFlowDocument, StartupStep } from '../types/graph'
import { useI18n } from '../i18n/I18nProvider'
import { ProjectPicker } from './ProjectPicker'

interface StartupFlowViewProps {
  flow: StartupFlowDocument
  selection: Selection | null
  onSelect: (key: string) => void
  projectKeys: string[]
  projectKey: string | null
  onChangeProject: (key: string) => void
}

/** Program.cs의 소스 순서를 서비스 구성과 요청 파이프라인으로 나눠 보여주는 흐름도다. */
export function StartupFlowView({
  flow,
  selection,
  onSelect,
  projectKeys,
  projectKey,
  onChangeProject,
}: StartupFlowViewProps) {
  const { t } = useI18n()
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  const selectedKey = selection?.type === 'startupStep' ? selection.key : null
  const isEmpty = flow.serviceSteps.length === 0 && flow.middlewareSteps.length === 0
  const topLevel = flow.serviceSteps.length + flow.middlewareSteps.length
  const total = countSteps(flow)
  const programName = fileName(flow.programFile)

  const toggle = (key: string) => setExpanded((current) => {
    const next = new Set(current)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    return next
  })

  return (
    <div className="graph-shell">
      <div className="view-intro startup-intro">
        <ProjectPicker projectKeys={projectKeys} projectKey={projectKey} onChange={onChangeProject} />
        <span className="sub">{t('startup.topLevelSteps', { top: topLevel, all: total })}</span>
      </div>
      {(flow.limitations ?? []).length > 0 && (
        <ul className="context-limitations">
          {(flow.limitations ?? []).map((line) => <li key={line}>{line}</li>)}
        </ul>
      )}
      {isEmpty && <div className="panel-message canvas-empty">{t('startup.empty')}</div>}
      {!isEmpty && (
        <div className="startup-diagram" aria-label={t('startup.diagramAria')}>
          <FlowTerminal tone="start" eyebrow={t('startup.start')} label={programName} />
          <StageConnector label={t('startup.sourceOrder')} />
          <StartupStage
            number={1}
            title={t('startup.services')}
            hint={t('startup.servicesHint')}
            code="builder.Services"
            steps={flow.serviceSteps}
            group="svc"
            maxColumns={4}
            expanded={expanded}
            onToggle={toggle}
            selectedKey={selectedKey}
            onSelect={onSelect}
          />
          <StageConnector label={t('startup.servicesComplete')} />
          <StartupStage
            number={2}
            title={t('startup.pipeline')}
            hint={t('startup.pipelineHint')}
            code="app"
            steps={flow.middlewareSteps}
            group="mw"
            maxColumns={3}
            expanded={expanded}
            onToggle={toggle}
            selectedKey={selectedKey}
            onSelect={onSelect}
          />
          <StageConnector label={t('startup.pipelineComplete')} />
          <FlowTerminal tone="end" eyebrow={t('startup.ready')} label={t('startup.run')} />
        </div>
      )}
    </div>
  )
}

function FlowTerminal({ tone, eyebrow, label }: { tone: 'start' | 'end'; eyebrow: string; label: string }) {
  return (
    <div className={`startup-terminal ${tone}`}>
      <span>{eyebrow}</span>
      <strong>{label}</strong>
    </div>
  )
}

function StageConnector({ label }: { label: string }) {
  return (
    <div className="startup-stage-connector" aria-hidden="true">
      <span>↓</span>
      <em>{label}</em>
    </div>
  )
}

interface StartupStageProps {
  number: number
  title: string
  hint: string
  code: string
  steps: StartupStep[]
  group: StartupGroup
  maxColumns: number
  expanded: ReadonlySet<string>
  onToggle: (key: string) => void
  selectedKey: string | null
  onSelect: (key: string) => void
}

function StartupStage({
  number,
  title,
  hint,
  code,
  steps,
  group,
  maxColumns,
  expanded,
  onToggle,
  selectedKey,
  onSelect,
}: StartupStageProps) {
  const { t } = useI18n()
  const columns = Math.max(1, Math.min(maxColumns, steps.length))
  const style = { '--startup-columns': columns } as CSSProperties

  return (
    <section className={`startup-stage ${group === 'svc' ? 'services' : 'pipeline'}`}>
      <header className="startup-stage-header">
        <span className="startup-stage-number">{number}</span>
        <div>
          <h3>{title}</h3>
          <p>{hint}</p>
        </div>
        <code>{code}</code>
        <span className="startup-stage-count">{t('startup.stepCount', { count: steps.length })}</span>
      </header>
      {steps.length === 0 && <div className="panel-message tight">{t('startup.noSteps')}</div>}
      {steps.length > 0 && (
        <ol className="startup-node-grid" style={style}>
          {steps.map((step, index) => {
            const row = Math.floor(index / columns)
            const offset = index % columns
            const column = row % 2 === 0 ? offset + 1 : columns - offset
            const isLast = index === steps.length - 1
            const isRowEnd = offset === columns - 1
            const direction = isLast ? 'none' : isRowEnd ? 'down' : row % 2 === 0 ? 'right' : 'left'

            return (
              <li
                key={startupStepKey(group, [index])}
                className={`startup-node-cell flow-${direction}`}
                style={{ gridColumn: column, gridRow: row + 1 }}
              >
                <StepNode
                  step={step}
                  path={startupStepKey(group, [index])}
                  sequence={index + 1}
                  expanded={expanded}
                  onToggle={onToggle}
                  selectedKey={selectedKey}
                  onSelect={onSelect}
                />
              </li>
            )
          })}
        </ol>
      )}
    </section>
  )
}

interface StepNodeProps {
  step: StartupStep
  path: string
  sequence?: number
  expanded: ReadonlySet<string>
  onToggle: (key: string) => void
  selectedKey: string | null
  onSelect: (key: string) => void
}

function StepNode({ step, path, sequence, expanded, onToggle, selectedKey, onSelect }: StepNodeProps) {
  const { t } = useI18n()
  const isExtension = step.kind === 'ExtensionCall'
  const isOpen = expanded.has(path)
  const selected = selectedKey === path
  const kindLabel = t(`detail.startup.${step.kind}`)

  return (
    <div className={`startup-node kind-${step.kind.toLowerCase()}${selected ? ' selected' : ''}`}>
      <button
        type="button"
        className="startup-node-button"
        onClick={() => {
          onSelect(path)
          if (isExtension && step.children.length > 0) onToggle(path)
        }}
        title={`${step.file}:${step.line}`}
        aria-expanded={isExtension && step.children.length > 0 ? isOpen : undefined}
      >
        <span className="startup-node-topline">
          {sequence !== undefined && <span className="startup-sequence">{sequence}</span>}
          <span className="startup-kind">{kindLabel}</span>
          {step.lifetime && (
            <span className={`startup-badge lifetime-${step.lifetime.toLowerCase()}`}>{step.lifetime}</span>
          )}
        </span>
        <span className="startup-node-label">{step.label}</span>
        {isExtension && step.children.length > 0 && (
          <span className="startup-expand-hint">
            <span className={`startup-caret${isOpen ? ' open' : ''}`}>▸</span>
            {isOpen ? t('startup.collapse') : t('startup.expand', { count: step.children.length })}
          </span>
        )}
      </button>
      {isExtension && isOpen && step.children.length > 0 && (
        <ol className="startup-branch">
          {step.children.map((child, index) => (
            <li key={`${path}.${index}`}>
              <StepNode
                step={child}
                path={`${path}.${index}`}
                expanded={expanded}
                onToggle={onToggle}
                selectedKey={selectedKey}
                onSelect={onSelect}
              />
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}

function fileName(path: string): string {
  const segments = path.split(/[\\/]/)
  return segments.at(-1) || path
}

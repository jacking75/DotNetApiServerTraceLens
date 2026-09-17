import { useState } from 'react'
import { startupStepKey, type StartupGroup } from '../graph/startupFlow'
import type { Selection, StartupFlowDocument, StartupStep } from '../types/graph'
import { useI18n } from '../i18n/I18nProvider'

interface StartupFlowViewProps {
  flow: StartupFlowDocument
  selection: Selection | null
  onSelect: (key: string) => void
}

/**
 * "시작 흐름" 보기. Program.cs를 소스 순서대로 읽어 서비스 등록(builder.Services)과
 * 미들웨어 파이프라인(app)을 타임라인 두 단으로 보여준다. 확장 메서드 호출은 접혀 있다가 누르면 펼쳐진다.
 */
export function StartupFlowView({ flow, selection, onSelect }: StartupFlowViewProps) {
  const { t } = useI18n()
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  const toggle = (key: string) => setExpanded((current) => {
    const next = new Set(current)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    return next
  })

  const selectedKey = selection?.type === 'startupStep' ? selection.key : null
  const isEmpty = flow.serviceSteps.length === 0 && flow.middlewareSteps.length === 0

  return (
    <div className="graph-shell">
      {isEmpty && <div className="panel-message canvas-empty">{t('startup.empty')}</div>}
      {!isEmpty && (
        <div className="startup-flow">
          <StartupSection
            title={t('startup.services')}
            hint="builder.Services"
            steps={flow.serviceSteps}
            group="svc"
            expanded={expanded}
            onToggle={toggle}
            selectedKey={selectedKey}
            onSelect={onSelect}
          />
          <StartupSection
            title={t('startup.pipeline')}
            hint="app"
            steps={flow.middlewareSteps}
            group="mw"
            expanded={expanded}
            onToggle={toggle}
            selectedKey={selectedKey}
            onSelect={onSelect}
          />
        </div>
      )}
    </div>
  )
}

interface StartupSectionProps {
  title: string
  hint: string
  steps: StartupStep[]
  group: StartupGroup
  expanded: ReadonlySet<string>
  onToggle: (key: string) => void
  selectedKey: string | null
  onSelect: (key: string) => void
}

function StartupSection({ title, hint, steps, group, expanded, onToggle, selectedKey, onSelect }: StartupSectionProps) {
  const { t } = useI18n()
  return (
    <div className="startup-section">
      <div className="startup-section-title">
        <span>{title}</span>
        <span className="startup-section-hint">{hint}</span>
        <span className="count">{steps.length}</span>
      </div>
      {steps.length === 0 && <div className="panel-message tight">{t('startup.noSteps')}</div>}
      {steps.length > 0 && (
        <ol className="startup-list">
          {steps.map((step, index) => (
            <StepRow
              key={index}
              step={step}
              path={startupStepKey(group, [index])}
              expanded={expanded}
              onToggle={onToggle}
              selectedKey={selectedKey}
              onSelect={onSelect}
            />
          ))}
        </ol>
      )}
    </div>
  )
}

interface StepRowProps {
  step: StartupStep
  path: string
  expanded: ReadonlySet<string>
  onToggle: (key: string) => void
  selectedKey: string | null
  onSelect: (key: string) => void
}

function StepRow({ step, path, expanded, onToggle, selectedKey, onSelect }: StepRowProps) {
  const isExtension = step.kind === 'ExtensionCall'
  const isOpen = expanded.has(path)
  const selected = selectedKey === path

  return (
    <li className={`startup-step kind-${step.kind.toLowerCase()}${selected ? ' selected' : ''}`}>
      <button
        type="button"
        className="startup-row"
        onClick={() => {
          onSelect(path)
          if (isExtension) onToggle(path)
        }}
        title={`${step.file}:${step.line}`}
      >
        {isExtension && <span className={`startup-caret${isOpen ? ' open' : ''}`}>▸</span>}
        <span className="startup-label">{step.label}</span>
        {step.lifetime && (
          <span className={`startup-badge lifetime-${step.lifetime.toLowerCase()}`}>{step.lifetime}</span>
        )}
        {isExtension && <span className="count">{step.children.length}</span>}
      </button>
      {isExtension && isOpen && step.children.length > 0 && (
        <ol className="startup-list nested">
          {step.children.map((child, index) => (
            <StepRow
              key={index}
              step={child}
              path={`${path}.${index}`}
              expanded={expanded}
              onToggle={onToggle}
              selectedKey={selectedKey}
              onSelect={onSelect}
            />
          ))}
        </ol>
      )}
    </li>
  )
}

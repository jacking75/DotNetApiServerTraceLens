import { useState } from 'react'
import { countSteps, startupStepKey, type StartupGroup } from '../graph/startupFlow'
import type { Selection, StartupFlowDocument, StartupStep } from '../types/graph'
import { useI18n } from '../i18n/I18nProvider'
import { ProjectPicker } from './ProjectPicker'

interface StartupFlowViewProps {
  flow: StartupFlowDocument
  selection: Selection | null
  onSelect: (key: string) => void
  /** 실행 프로젝트가 여럿이면 골라 본다. 하나뿐이면 선택이 보이지 않는다. */
  projectKeys: string[]
  projectKey: string | null
  onChangeProject: (key: string) => void
}

/**
 * "시작 흐름" 보기. Program.cs를 소스 순서대로 읽어 서비스 등록(builder.Services)과
 * 미들웨어 파이프라인(app)을 타임라인 두 단으로 보여준다. 확장 메서드 호출은 접혀 있다가 누르면 펼쳐진다.
 */
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
  const toggle = (key: string) => setExpanded((current) => {
    const next = new Set(current)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    return next
  })

  const selectedKey = selection?.type === 'startupStep' ? selection.key : null
  const isEmpty = flow.serviceSteps.length === 0 && flow.middlewareSteps.length === 0

  // 단계 수는 한 정의만 쓴다. 좌측이 "22개", 가운데가 "19단계"면 어느 쪽을 믿어야 할지 알 수 없다.
  const topLevel = flow.serviceSteps.length + flow.middlewareSteps.length
  const total = countSteps(flow)

  return (
    <div className="graph-shell">
      <div className="view-intro">
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

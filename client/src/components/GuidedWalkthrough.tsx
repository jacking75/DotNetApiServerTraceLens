import { useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'

/** 안내가 추적하는 첫 성공 경로의 단계 ID. 좌표가 아니라 실제 완료 이벤트로 진행한다. */
export type WalkthroughStepId = 'pick' | 'analyze' | 'entry' | 'evidence' | 'context'

interface WalkthroughStep {
  id: WalkthroughStepId
  titleKey: TranslationKey
  bodyKey: TranslationKey
  /** 안내가 가리키는 UI 요소의 id. 좌표 대신 요소 id로 연결한다. */
  targetElementId: string
}

const steps: readonly WalkthroughStep[] = [
  { id: 'pick', titleKey: 'guide.step.pick', bodyKey: 'guide.step.pickBody', targetElementId: 'tracelens-target-input' },
  { id: 'analyze', titleKey: 'guide.step.analyze', bodyKey: 'guide.step.analyzeBody', targetElementId: 'tracelens-analyze-button' },
  { id: 'entry', titleKey: 'guide.step.entry', bodyKey: 'guide.step.entryBody', targetElementId: 'tracelens-side-panel' },
  { id: 'evidence', titleKey: 'guide.step.evidence', bodyKey: 'guide.step.evidenceBody', targetElementId: 'tracelens-detail-panel' },
  { id: 'context', titleKey: 'guide.step.context', bodyKey: 'guide.step.contextBody', targetElementId: 'tracelens-ask-panel' },
]

interface GuidedWalkthroughProps {
  /** 이미 끝낸 단계. 사용자의 실제 선택·복사 완료로만 채워진다. */
  completed: ReadonlySet<WalkthroughStepId>
  onClose: () => void
}

/**
 * 첫 성공 경로 안내다. 언제든 닫고 다시 열 수 있고, 진행은 실제 완료 이벤트로만 올라간다.
 * 화면 좌표에 의존하지 않으므로 창 크기가 바뀌어도 어긋나지 않는다.
 */
export function GuidedWalkthrough({ completed, onClose }: GuidedWalkthroughProps) {
  const { t } = useI18n()
  const current = steps.find((step) => !completed.has(step.id)) ?? steps[steps.length - 1]

  const focusTarget = (elementId: string) => {
    const element = document.getElementById(elementId)
    element?.scrollIntoView({ block: 'nearest' })
    if (element instanceof HTMLElement) element.focus({ preventScroll: true })
  }

  return (
    <aside className="walkthrough" aria-label={t('guide.title')}>
      <header>
        <span>{t('guide.title')}</span>
        <small>{t('guide.progress', { done: completed.size, total: steps.length })}</small>
        <button type="button" className="link-button" onClick={onClose}>{t('guide.close')}</button>
      </header>
      <ol>
        {steps.map((step) => {
          const done = completed.has(step.id)
          return (
            <li key={step.id} className={done ? 'done' : step.id === current.id ? 'current' : ''}>
              <button type="button" className="link-button" onClick={() => focusTarget(step.targetElementId)}>
                {t(step.titleKey)}
              </button>
              {step.id === current.id && !done && <div className="hint">{t(step.bodyKey)}</div>}
              {done && <span className="check" aria-label={t('guide.done')}>✓</span>}
            </li>
          )
        })}
      </ol>
    </aside>
  )
}

export const walkthroughStepIds = steps.map((step) => step.id)
